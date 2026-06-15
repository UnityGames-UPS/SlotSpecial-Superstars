using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections;

public class OrientationChange : MonoBehaviour
{
  [SerializeField] private RectTransform UIWrapper;
  [SerializeField] private CanvasScaler CanvasScaler;
  [SerializeField] private float MatchWidth = 0f;
  [SerializeField] private float MatchHeight = 1f;
  [SerializeField] private float PortraitMatchHeight = 1f;
  [SerializeField] private float transitionDuration = 0.2f;
  [SerializeField] private float waitForRotation = 0.2f;

  private Vector2 ReferenceAspect;
  private Tween matchTween;
  private Tween rotationTween;
  private Coroutine rotationRoutine;
  private bool isLandscape;
  private void Awake()
  {
    ReferenceAspect = CanvasScaler.referenceResolution;
  }

  void SwitchDisplay(string dimensions)
  {
    if (rotationRoutine != null) StopCoroutine(rotationRoutine);
    rotationRoutine = StartCoroutine(RotationCoroutine(dimensions));
  }

  IEnumerator RotationCoroutine(string dimensions)
  {
    yield return new WaitForSecondsRealtime(waitForRotation);
    string[] parts = dimensions.Split(',');
    if (parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height) && width > 0 && height > 0)
    {
      Debug.LogWarning($"Unity: Received Dimensions - Width: {width}, Height: {height}");

      isLandscape = width > height;

      Quaternion targetRotation = isLandscape ? Quaternion.identity : Quaternion.Euler(0, 0, -90);
      if (rotationTween != null && rotationTween.IsActive()) rotationTween.Kill();
      rotationTween = UIWrapper.DOLocalRotateQuaternion(targetRotation, transitionDuration).SetEase(Ease.OutCubic);

      float currentAspectRatio = isLandscape ? (float)width / height : (float)height / width;
      float referenceAspectRatio = ReferenceAspect.x / ReferenceAspect.y;
      Debug.LogWarning("currentAspect Ratio: " + currentAspectRatio);
      float targetMatch;

      if (isLandscape)
      {
        targetMatch = currentAspectRatio > referenceAspectRatio ? MatchHeight : MatchWidth;
      }
      else
      {
        if (currentAspectRatio >= 1.3f && currentAspectRatio < 1.4f)
          targetMatch = 0.27f;   // ~1.3
        else if (currentAspectRatio >= 1.4f && currentAspectRatio < 1.5f)
          targetMatch = 0.32f;   // ~1.4
        else if (currentAspectRatio >= 1.5f && currentAspectRatio < 1.6f)
          targetMatch = 0.34f;   // ~1.5
        else if (currentAspectRatio >= 1.6f && currentAspectRatio < 1.85f)
          targetMatch = 0.53f;    // ~2.0 range
        else if (currentAspectRatio >= 1.85 && currentAspectRatio < 2.4)
          targetMatch = 0.5f;
        else if(currentAspectRatio >= 2.4 && currentAspectRatio < 2.7)
          targetMatch = 0.45f;
        else
          targetMatch = PortraitMatchHeight;
      }

      if (matchTween != null && matchTween.IsActive()) matchTween.Kill();
      matchTween = DOTween.To(() => CanvasScaler.matchWidthOrHeight, x => CanvasScaler.matchWidthOrHeight = x, targetMatch, transitionDuration).SetEase(Ease.InOutQuad);

      Debug.LogWarning($"matchWidthOrHeight set to: {targetMatch}");
    }
    else
    {
      Debug.LogWarning("Unity: Invalid format received in SwitchDisplay");
    }
  }

#if UNITY_EDITOR
  private void Update()
  {
    if (Input.GetKeyDown(KeyCode.Space))
    {
      SwitchDisplay(Screen.width + "," + Screen.height);
    }
  }
#endif
}
