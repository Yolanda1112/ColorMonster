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
//
// 追加（可視化のためのデバッグ情報）:
//  - DebugChannels[0..3] に各chの最新値・単色判定・最終判定を入れる
//    → UI(HUD)側はこの配列を読むだけで「生RGB」と「判定色」を表示できる

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

    [Header("RAW Thresholds (simple)")]
    [SerializeField] private int rawRedMin = 100;
    [SerializeField] private int rawBlueMin = 100;
    [SerializeField] private int rawYellowRMin = 100;
    [SerializeField] private int rawYellowGMin = 220;

    [Header("Stability")]
    // 判定がフラフラするとゲームが暴れるので「安定化」させる
    // 同じ結果が連続 stableFrames 回続いたら “確定” とする
    [SerializeField] private int stableFrames = 3;

    // 確定を連打しないためのクールダウン（ms）
    [SerializeField] private int applyCooldownMs = 120;

    // =========================
    // 可視化のための公開デバッグ情報
    // =========================
    //
    // HUD側（Canvas上のUIなど）は、この DebugChannels を読むだけで
    //  - 生RGB（raw）
    //  - chごとの単色判定（base）
    //  - 全chを統合した最終判定（final）
    // をゲーム画面上に出せる
    //
    [System.Serializable]
    public struct ChannelDebug
    {
        public bool hasData;      // そのchに「最近のデータ」があるか
        public int r, g, b, c;    // 生値
        public string baseColor;  // "red/blue/yellow/none"
        public string finalColor; // "red/blue/yellow/purple/orange/green/none"
    }

    // 外部（HUDスクリプトなど）から読むためのプロパティ
    // 配列サイズは常に 4 に固定（ch0..ch3想定）※maxChannelsが小さくても4を返す
    public ChannelDebug[] DebugChannels { get; private set; } = new ChannelDebug[4];

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

        // 最新サンプル配列を初期化（0除け）
        int n = Mathf.Clamp(maxChannels, 1, 4);
        _latest = new Sample[n];

        // DebugChannelsも初期化（HUDが読むので常に4要素）
        DebugChannels = new ChannelDebug[4];
        for (int i = 0; i < DebugChannels.Length; i++)
        {
            DebugChannels[i] = new ChannelDebug
            {
                hasData = false,
                r = 0, g = 0, b = 0, c = 0,
                baseColor = "none",
                finalColor = "none"
            };
        }

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
                // いつ受信したデータかを記録（タイムアウト判定に使う）
                s.time = Time.time;

                // ch範囲内だけ更新
                if (s.ch >= 0 && s.ch < _latest.Length)
                {
                    _latest[s.ch] = s;
                }

                // HUD向けデバッグ情報も「生値だけ先に」更新しておく
                // ※maxChannels<4のときも DebugChannels は4要素あるので、範囲チェックする
                if (s.ch >= 0 && s.ch < DebugChannels.Length)
                {
                    DebugChannels[s.ch] = new ChannelDebug
                    {
                        hasData = true,
                        r = s.r, g = s.g, b = s.b, c = s.c,
                        baseColor = DebugChannels[s.ch].baseColor,   // 後で上書きする
                        finalColor = DebugChannels[s.ch].finalColor  // 後で上書きする
                    };
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
        {
            baseColors[ch] = JudgeBaseColor(_latest[ch]);

            // HUD向け：単色判定（base）を埋める
            if (ch >= 0 && ch < DebugChannels.Length)
            {
                var d = DebugChannels[ch];
                d.hasData = d.hasData || (_latest[ch].time > 0);
                d.baseColor = BaseColorToName(baseColors[ch]);
                DebugChannels[ch] = d;
            }
        }

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

        // HUD向け：最終判定（final）を全chに入れる
        string finalName = IndexToName(decided);
        for (int ch = 0; ch < DebugChannels.Length; ch++)
        {
            var d = DebugChannels[ch];

            // maxChannelsより外のchは「常にnone」にしておく（HUDが4ch固定表示でも破綻しない）
            if (ch >= _latest.Length)
            {
                d.hasData = false;
                d.r = d.g = d.b = d.c = 0;
                d.baseColor = "none";
                d.finalColor = "none";
                DebugChannels[ch] = d;
                continue;
            }

            d.finalColor = finalName;
            DebugChannels[ch] = d;
        }

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
            Debug.Log($"[ColorSensor] Applied attackColor={decided} ({finalName})");
        }
    }

    // =========================
    // 判定ロジック：単色判定
    // =========================
    //
    // 1ch分の RGB を見て red / blue / yellow / none のどれかにする
    //
    // ここでは 2種類の判定方法を持つ:
    //  - HSV(Hue)ベース（useHSVClassification = true）
    //  - 旧方式：RGB比率しきい値ベース（useHSVClassification = false）
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

        // 黄色を先に取る（R>100 を満たす黄色が赤に化けるのを防ぐ）
        if (s.r >= rawYellowRMin && s.g >= rawYellowGMin) return BaseColor.Yellow;

        // 次に青
        if (s.b >= rawBlueMin) return BaseColor.Blue;

        // 最後に赤
        if (s.r >= rawRedMin) return BaseColor.Red;

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
    //
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
    // 文字列変換（HUD用・ログ用）
    // =========================
    private static string BaseColorToName(BaseColor bc)
    {
        switch (bc)
        {
            case BaseColor.Red: return "red";
            case BaseColor.Blue: return "blue";
            case BaseColor.Yellow: return "yellow";
            default: return "none";
        }
    }

    private static string IndexToName(int idx)
    {
        switch (idx)
        {
            case 0: return "red";
            case 1: return "blue";
            case 2: return "yellow";
            case 3: return "purple";
            case 4: return "orange";
            case 5: return "green";
            default: return "none";
        }
    }

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
