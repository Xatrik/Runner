using UnityEngine;

/// <summary>
/// Animation Events only invoke methods on components attached to the same
/// GameObject as the Animator that's playing the clip. The kick animation lives
/// on the leg armature, not on the Player, so it can't call Player.KickContact()
/// directly — this relay sits on the armature and forwards the call up.
///
/// Setup: put this component on the same GameObject as the leg's Animator.
/// In the Animation window, add an Animation Event on the kick clip's foot-
/// contact frame and point it at OnFootContact.
/// </summary>
public class LegKickRelay : MonoBehaviour
{
    [Tooltip("Auto-found on a parent if left empty.")]
    [SerializeField] private Player player;

    private void Awake()
    {
        if (player == null)
            player = GetComponentInParent<Player>();
    }

    // Call this from the Animation Event on the kick clip's contact frame.
    public void OnFootContact()
    {
        if (player != null)
            player.KickContact();
        else
            Debug.LogWarning($"{name}: LegKickRelay has no Player to notify — assign one or move this under the Player.", this);
    }
}
