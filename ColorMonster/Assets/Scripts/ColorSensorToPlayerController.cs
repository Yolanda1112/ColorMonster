// ファイル名: ColorSensorToPlayerController.cs
//
// 目的:
//  1) ESP32(Arduino)から送られてくるカラーセンサ値をシリアルで受信
//  2) 各チャンネル(ch0..ch3)を「red / blue / yellow / none」に分類
//  3) 複数チャンネルが同時に検出された場合は混色(purple/orange/green)として確定
//  4) 確定した色を PlayerController の attackColor に上書き（キーボード操作は残す）
//
// Arduino側の想定出力（1行=1サンプル）:
//    "ch,red,green,blue,clear\n"
// 例:
//    0,120,30,10,200
//    1,10,20,140,210
//
// PlayerController側は index を攻撃色として使っている：
//   0:red, 1:blue, 2:yellow, 3:purple, 4:orange, 5:green
//
// 注意:
//  - COMポートは「同時に複数スクリプトでOpenできない」ので、SerialPortを開くスクリプトはこれ1つにする
//  - PlayerController に public void SetAttackColorExternal(int idx) が必要（private attackColor を外部から変えるため）

using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Threading;
using UnityEngine;

public class ColorSensorToPlayerController : MonoBehaviour
{
    // =========================
    // Inspectorで設定するパラメータ群
    // =========================

    [Header("References")]
    // 攻撃色を反映したい相手（PlayerController）を指定する
    // Inspectorでドラッグ&ドロップするのが確実
    [SerializeField] private PlayerController player;

    [Header("Serial Settings")]
    // ESP32が見えているCOMポート（WindowsならCOM3など）
    [SerializeField] private string portName = "COM3";
    // Arduino側のSerial.begin(115200) と一致させる
    [SerializeField] private int baudRate = 115200;

    // DTR/RTSは「ポートオープン時にESP32がリセットされる挙動」に影響する
    // 自動リセットを避けたいなら false で試すことが多い
    [SerializeField] private bool dtrEnable = false;
    [SerializeField] private bool rtsEnable = false;

    [Header("Channels")]
    // マルチプレクサで最大4ch読む想定（0..3）
    [SerializeField] private int maxChannels = 4;

    // 受信が止まったチャンネルを「無効」とみなすためのタイムアウト（ms）
    // 例: 700ms以上更新が無ければ、そのchは None 扱いにする
    [SerializeField] private int noDataTimeoutMs = 700;

    // clearが小さい=暗い/何もかざしてない/LED消えてる などの可能性
    // ある程度より小さいときは判定しない（None）
    [SerializeField] private int minClear = 10;

    [Header("Classification Thresholds (tune)")]
    // RGBの正規化値で「何色っぽいか」を判定する閾値
    // 現場ではここは調整ポイント（カードの材質/LED/環境光で変わる）
    [SerializeField] private float redDominantMin = 0.55f;
    [SerializeField] private float blueDominantMin = 0.50f;
    [SerializeField] private float yellowRGMin = 0.35f;
    [SerializeField] private float yellowBMax = 0.25f;

    [Header("Stability")]
    // 判定がフラフラするとゲームが暴れるので「安定化」させる
    // 同じ結果が連続 stableFrames 回続いたら “確定” とする
    [SerializeField] private int stableFrames = 3;

    // 確定を連打しないためのクールダウン（ms）
    [SerializeField] private int applyCooldownMs = 120;

    // =========================
    // シリアル受信のための内部状態
    // =========================

    // 実際のシリアルポート
    private SerialPort _sp;

    // シリアル受信は別スレッドで動かす（メインスレッドを止めないため）
    private Thread _readThread;

    // スレッド停止指示用
    private volatile bool _running;

    // 受信した「1行」をメインスレッドに渡すキュー（スレッド安全）
    private readonly ConcurrentQueue<string> _lineQueue = new ConcurrentQueue<string>();

    // 1バイトずつ読んで、\n まで溜めるためのバッファ
    private readonly StringBuilder _sb = new StringBuilder(128);

    // 各chの「最新サンプル」を保持する（ch=0..maxChannels-1）
    private Sample[] _latest;

    // 安定化（同じ判定が何回続いたか）
    private int _stableCount = 0;
    private int _stableIndex = -2;      // “いま安定候補になってる色index”
    private int _lastAppliedIndex = -2; // “最後にPlayerControllerへ適用した色index”
    private int _lastApplyMs = -999999; // “最後に適用した時刻（Environment.TickCount）”

    // =========================
    // 判定用の型
    // =========================

    // まず各chを「基本3色 + none」に落とすためのenum
    private enum BaseColor { None = -1, Red = 0, Blue = 1, Yellow = 2 }

    // 受信した1行をパースした結果
    // r,g,b,c は 0..255 を想定（BH1745を8bitモードにしている前提）
    private struct Sample
    {
        public int ch;
        public int r, g, b, c;
        public float time; // Time.time（何秒経過か）
    }

    // =========================
    // Unityライフサイクル
    // =========================

    void Start()
    {
        // playerがInspectorで指定されてない場合はシーンから探す（保険）
        if (player == null) player = FindObjectOfType<PlayerController>();

        // 最新サンプル配列を初期化
        _latest = new Sample[Mathf.Max(1, maxChannels)];

        // シリアルを開く（失敗すると例外ログが出る）
        OpenSerial();

        // ESP32はポートOpen直後にリセットがかかることがあるので少し待ってから受信開始
        StartReadThreadAfterDelay(800);
    }

    void OpenSerial()
    {
        try
        {
            _sp = new SerialPort(portName, baudRate)
            {
                NewLine = "\n",
                ReadTimeout = 50,   // ReadByteが来ないときにすぐ戻るため
                WriteTimeout = 50,
                DtrEnable = dtrEnable,
                RtsEnable = rtsEnable,
                Encoding = Encoding.ASCII
            };
            _sp.Open();
            Debug.Log($"[ColorSensor] Opened {portName} @ {baudRate}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ColorSensor] Open failed: {ex.Message}");
        }
    }

    // “少し待ってから”受信スレッドを立てる（ESP32の自動リセット対策）
    async void StartReadThreadAfterDelay(int ms)
    {
        await System.Threading.Tasks.Task.Delay(ms);

        // ポートが開いてなければ何もしない
        if (_sp == null || !_sp.IsOpen) return;

        _running = true;

        // 受信スレッド開始
        _readThread = new Thread(ReadLoop) { IsBackground = true };
        _readThread.Start();
    }

    // =========================
    // 受信スレッド側：ReadLoop
    // =========================
    //
    // ここは「別スレッド」で動く。
    // Unityのオブジェクトに触るのはNGなので、
    // 受け取った行は _lineQueue に入れるだけにしている。
    //
    void ReadLoop()
    {
        while (_running)
        {
            try
            {
                // 1バイトずつ読み取る
                // データがないとTimeoutExceptionになってループ継続
                int b = _sp.ReadByte();
                if (b < 0) continue;

                char c = (char)b;

                // 改行(\n)が来たら、1行が完成したとみなす
                if (c == '\n')
                {
                    // 行として取り出し（\r や空白を除去）
                    string line = _sb.ToString().Trim('\r', '\n', ' ');

                    // バッファをクリアして次の行へ
                    _sb.Length = 0;

                    // 空行じゃなければキューへ
                    if (!string.IsNullOrEmpty(line)) _lineQueue.Enqueue(line);
                }
                else
                {
                    // \n が来るまでバッファに貯める
                    // 事故で超長い行が来たときにメモリが暴れないよう上限を設ける
                    if (_sb.Length < 256) _sb.Append(c);
                    else _sb.Length = 0;
                }
            }
            catch (TimeoutException)
            {
                // 何も来てないだけ。無視してループ継続。
            }
            catch (Exception ex)
            {
                // ポート抜け・切断などの可能性
                Debug.LogWarning($"[ColorSensor] Read error: {ex.Message}");
                Thread.Sleep(50);
            }
        }
    }

    // =========================
    // Unityメインスレッド：Update
    // =========================
    //
    // ここで「受信結果を処理」「色判定」「PlayerControllerへ反映」を行う。
    //
    void Update()
    {
        // -------- 1) 受信行を取り込んで最新値を更新 --------
        //
        // ReadLoopが _lineQueue に入れた “完成した行” を全部取り出す
        // 取り出した行は CSV パースして chごとの _latest[] を更新する
        //
        while (_lineQueue.TryDequeue(out var line))
        {
            Debug.Log($"[ColorSensor RAW] {line}");
            if (TryParseLine(line, out var s))
            {
                if (s.ch >= 0 && s.ch < _latest.Length)
                {
                    // いつ受信したデータかを記録（タイムアウト判定に使う）
                    s.time = Time.time;
                    _latest[s.ch] = s;
                }
            }
        }

        // -------- 2) 各chを基本色（red/blue/yellow/none）に分類 --------
        //
        // ここでは「chごとの判定」だけ
        // 混色は次のステップでまとめて判断する
        //
        BaseColor[] baseColors = new BaseColor[_latest.Length];
        for (int ch = 0; ch < _latest.Length; ch++)
            baseColors[ch] = JudgeBaseColor(_latest[ch]);

        // -------- 3) 複数chの結果から最終色(index)を決定 --------
        //
        // {red,blue} -> purple(3)
        // {red,yellow} -> orange(4)
        // {blue,yellow} -> green(5)
        //
        // 単色のみなら 0/1/2
        // 何も判定できなければ -1
        //
        int decided = DecideMixedIndex(baseColors);

        // -------- 4) 安定化（連続stableFramesで確定） --------
        //
        // センサ値は揺れるので、確定判定を “連続回数” で落ち着かせる
        //
        if (decided == _stableIndex) _stableCount++;
        else { _stableIndex = decided; _stableCount = 1; }

        // 連続回数が足りないならまだ確定しない
        if (_stableCount < stableFrames) return;

        // -------- 5) PlayerControllerへ反映（クールダウン + 変化時のみ） --------
        //
        // 連打で色が暴れないように applyCooldownMs を入れている
        // 同じ色なら何もしない
        //
        int now = Environment.TickCount;
        if (now - _lastApplyMs < applyCooldownMs) return;
        if (decided == _lastAppliedIndex) return;

        // 判定不能(-1)のときは「何もしない」設定
        // “カードが離れたら色をnullに戻したい”ならここを変える
        if (decided < 0) return;

        // PlayerControllerが見つかっていれば、攻撃色を上書き
        if (player != null)
        {
            // ★ここが外部から attackColor を書き換える唯一の場所
            // PlayerControllerに SetAttackColorExternal が必要
            player.SetAttackColorExternal(decided);

            _lastAppliedIndex = decided;
            _lastApplyMs = now;
            Debug.Log($"[ColorSensor] Applied attackColor={decided}");
        }
    }

    // =========================
    // 判定ロジック：単色判定
    // =========================
    //
    // 1ch分の RGB を見て red / blue / yellow / none のどれかにする
    // 「明るさの影響」を減らすため、rgbを sum で割って正規化している
    //
    private BaseColor JudgeBaseColor(Sample s)
    {
        // 受信したことがない初期状態
        if (s.time <= 0) return BaseColor.None;

        // 古いデータなら無効（センサが途切れた等）
        float ageMs = (Time.time - s.time) * 1000f;
        if (noDataTimeoutMs > 0 && ageMs > noDataTimeoutMs) return BaseColor.None;

        // 暗すぎるなら無効
        if (s.c < minClear) return BaseColor.None;

        // ----- 正規化 -----
        // 明るさが変わっても判定が崩れにくいよう
        // r,g,b を (r+g+b) で割って割合にする
        float sum = (float)(s.r + s.g + s.b) + 1f; // 0除算防止で+1
        float rn = s.r / sum;
        float gn = s.g / sum;
        float bn = s.b / sum;

        // ----- red判定 -----
        // Rが十分強く、G/Bが弱い
        if (rn >= redDominantMin && gn < 0.30f && bn < 0.30f) return BaseColor.Red;

        // ----- blue判定 -----
        // Bが十分強く、R/Gが弱い
        if (bn >= blueDominantMin && rn < 0.35f && gn < 0.35f) return BaseColor.Blue;

        // ----- yellow判定 -----
        // 黄色は “RとGが両方強く、Bが弱い” という特徴で拾う
        if (rn >= yellowRGMin && gn >= yellowRGMin && bn <= yellowBMax) return BaseColor.Yellow;

        // どれにも当てはまらないなら none
        return BaseColor.None;
    }

    // =========================
    // 判定ロジック：混色判定（複数chまとめ）
    // =========================
    //
    // 各chの BaseColor 結果を見て、最終的な攻撃色indexにする
    //
    private int DecideMixedIndex(BaseColor[] baseColors)
    {
        bool hasR = false, hasB = false, hasY = false;

        // “どの基本色が存在したか” を集合として持つ
        foreach (var bc in baseColors)
        {
            if (bc == BaseColor.Red) hasR = true;
            else if (bc == BaseColor.Blue) hasB = true;
            else if (bc == BaseColor.Yellow) hasY = true;
        }

        int count = (hasR ? 1 : 0) + (hasB ? 1 : 0) + (hasY ? 1 : 0);

        // 何も判定できない
        if (count == 0) return -1;

        // 単色
        if (count == 1)
        {
            if (hasR) return 0; // red
            if (hasB) return 1; // blue
            return 2;           // yellow
        }

        // 2色混色
        if (count == 2)
        {
            if (hasR && hasB) return 3; // purple
            if (hasR && hasY) return 4; // orange
            if (hasB && hasY) return 5; // green
        }

        // 3色同時は判定しない（必要ならルールを追加）
        return -1;
    }

    // =========================
    // CSVパース
    // =========================
    //
    // "ch,r,g,b,c" を int にする
    //
// CSVパース（互換対応）
// - "ch,r,g,b,c"   (5要素)  -> 通常
// - "r,g,b,c"      (4要素)  -> ch=0 として扱う（単一センサ運用）
// 例: "222,250,45,79" -> ch=0, r=222, g=250, b=45, c=79
    private static bool TryParseLine(string line, out Sample s)
    {
    s = default;

    var parts = line.Split(',');
    if (parts.Length == 5)
    {
        if (!TryInt(parts[0], out s.ch)) return false;
        if (!TryInt(parts[1], out s.r)) return false;
        if (!TryInt(parts[2], out s.g)) return false;
        if (!TryInt(parts[3], out s.b)) return false;
        if (!TryInt(parts[4], out s.c)) return false;
        return true;
    }
    else if (parts.Length == 4)
    {
        // ch無しのときは ch=0 に固定
        s.ch = 0;
        if (!TryInt(parts[0], out s.r)) return false;
        if (!TryInt(parts[1], out s.g)) return false;
        if (!TryInt(parts[2], out s.b)) return false;
        if (!TryInt(parts[3], out s.c)) return false;
        return true;
    }

    return false;
    }


    private static bool TryInt(string str, out int v)
        => int.TryParse(str.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

    // =========================
    // 終了処理：スレッドとシリアルをちゃんと閉じる
    // =========================
    void OnApplicationQuit()
    {
        _running = false;

        // 受信スレッドを止める
        try
        {
            if (_readThread != null && _readThread.IsAlive)
            {
                _readThread.Join(200);
                _readThread = null;
            }
        }
        catch { }

        // シリアルポートを閉じる
        try
        {
            if (_sp != null)
            {
                if (_sp.IsOpen) _sp.Close();
                _sp.Dispose();
                _sp = null;
            }
        }
        catch { }
    }
}
