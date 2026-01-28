using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance { get; private set; }
    public static AudioManager I => Instance;


    [Header("Audio Sources")]
    [SerializeField] private AudioSource _bgmSource;
    [SerializeField] private AudioSource _seSource;

    [Header("SE Clips")]
    [SerializeField] private AudioClip _seAttackSuccess;
    [SerializeField] private AudioClip _seDamage;
    [SerializeField] private AudioClip _seGameOver;
    [SerializeField] private AudioClip _seButton;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void PlayBGM(AudioClip clip, float volume = 1f)
    {
        if (_bgmSource == null || clip == null) return;

        if (_bgmSource.clip == clip && _bgmSource.isPlaying) return;

        _bgmSource.clip = clip;
        _bgmSource.loop = true;
        _bgmSource.volume = volume;
        _bgmSource.Play();
    }

    public void StopBGM()
    {
        if (_bgmSource == null) return;
        _bgmSource.Stop();
        _bgmSource.clip = null;
    }

    public void PlaySE(AudioClip clip, float volume = 1f)
    {
        if (_seSource == null || clip == null) return;
        _seSource.PlayOneShot(clip, volume);
    }

    public void PlayAttackSuccessSE() => PlaySE(_seAttackSuccess);
    public void PlayDamageSE() => PlaySE(_seDamage);
    public void PlayGameOverSE() => PlaySE(_seGameOver);
    public void PlayButtonSE() => PlaySE(_seButton); 

}
