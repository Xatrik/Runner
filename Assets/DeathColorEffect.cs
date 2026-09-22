using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Drives the scene's Global Volume "Color Adjustments" saturation toward a
/// target over time — used for the death fade. Needs a Volume in the scene
/// (e.g. the Global Volume) whose profile has a Color Adjustments override;
/// auto-found via FindObjectOfType if not assigned.
/// </summary>
public class DeathColorEffect : MonoBehaviour
{
    [Tooltip("Volume whose profile holds the Color Adjustments override. Auto-found in the scene if left empty.")]
    [SerializeField] private Volume volume;
    [Tooltip("How fast saturation chases the target, in saturation-units/second (range is -100..100).")]
    [SerializeField] private float lerpSpeed = 140f;
    [Tooltip("Saturation used at amount = 0 (normal). Applied immediately on Awake.")]
    [Range(-100f, 100f)]
    [SerializeField] private float defaultSaturation = 0f;
    [Tooltip("Saturation used at amount = 1 (e.g. on death). -100 is fully grayscale.")]
    [Range(-100f, 100f)]
    [SerializeField] private float targetSaturation = -100f;

    private ColorAdjustments colorAdjustments;
    private float amount; // 0 = defaultSaturation, 1 = targetSaturation

    private void Awake()
    {
        if (volume == null)
            volume = FindObjectOfType<Volume>();

        if (volume != null && volume.profile != null && volume.profile.TryGet(out colorAdjustments))
        {
            colorAdjustments.saturation.overrideState = true; // so our writes actually take effect
            colorAdjustments.saturation.value = defaultSaturation;
        }
        else
        {
            Debug.LogWarning($"{name}: no Volume with a Color Adjustments override found.", this);
        }
    }

    /// 0 = defaultSaturation, 1 = targetSaturation.
    public void SetTarget(float value) => amount = Mathf.Clamp01(value);

    private void Update()
    {
        if (colorAdjustments == null) return;

        // Unscaled so the fade still plays through a hit-stop.
        float goal = Mathf.Lerp(defaultSaturation, targetSaturation, amount);
        colorAdjustments.saturation.value = Mathf.MoveTowards(
            colorAdjustments.saturation.value, goal, lerpSpeed * Time.unscaledDeltaTime);
    }
}
