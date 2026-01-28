using UnityEngine;

public class ButtonClickSE : MonoBehaviour
{
    public void PlayClickSE()
    {
        if (AudioManager.Instance == null) return;
        AudioManager.Instance.PlayButtonSE(); 
    }
}
