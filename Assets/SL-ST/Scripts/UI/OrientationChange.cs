using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System.Collections;

public class OrientationChange : MonoBehaviour
{
  [SerializeField] private RectTransform UIWrapper;
  [SerializeField] private CanvasScaler CanvasScaler;

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

  private void Start()
  {
    ApplyMatch(Screen.width, Screen.height);
  }

  private void SwitchDisplay(string dimensions)
  {
    if (rotationRoutine != null) StopCoroutine(rotationRoutine);
    rotationRoutine = StartCoroutine(RotationCoroutine(dimensions));
  }

  private IEnumerator RotationCoroutine(string dimensions)
  {
    yield return new WaitForSecondsRealtime(waitForRotation);
    string[] parts = dimensions.Split(',');
    if (parts.Length == 2 && int.TryParse(parts[0], out int width) && int.TryParse(parts[1], out int height) && width > 0 && height > 0)
    {
      ApplyMatch(width, height);
    }
    else
    {
      Debug.LogWarning("Unity: Invalid format received in SwitchDisplay");
    }
  }

  private void ApplyMatch(int width, int height)
  {
    isLandscape = width > height;

    Quaternion targetRotation = isLandscape ? Quaternion.identity : Quaternion.Euler(0, 0, -90);
    if (rotationTween != null && rotationTween.IsActive()) rotationTween.Kill();
    rotationTween = UIWrapper.DOLocalRotateQuaternion(targetRotation, transitionDuration).SetEase(Ease.OutCubic);

    float refW = ReferenceAspect.x;
    float refH = ReferenceAspect.y;

    float widthScale = (float)width / refW;
    float heightScale = (float)height / refH;

    float targetScale;
    if (isLandscape)
    {
      targetScale = Mathf.Min(widthScale, heightScale);
    }
    else
    {
      // In portrait, the canvas is rotated -90 degrees.
      // So UI reference width (refW) aligns with screen height, and UI reference height (refH) aligns with screen width.
      float portraitWidthScale = (float)height / refW;
      float portraitHeightScale = (float)width / refH;
      targetScale = Mathf.Min(portraitWidthScale, portraitHeightScale);
    }

    float targetMatch;
    if (Mathf.Abs(heightScale - widthScale) < 0.0001f)
    {
      targetMatch = 0.5f;
    }
    else
    {
      float logRatio = Mathf.Log(heightScale / widthScale);
      targetMatch = Mathf.Log(targetScale / widthScale) / logRatio;
      targetMatch = Mathf.Clamp01(targetMatch);
    }

    if (matchTween != null && matchTween.IsActive()) matchTween.Kill();
    matchTween = DOTween.To(() => CanvasScaler.matchWidthOrHeight, x => CanvasScaler.matchWidthOrHeight = x, targetMatch, transitionDuration).SetEase(Ease.InOutQuad);

    Debug.LogWarning($"Unity: Dimensions {width}x{height}, isLandscape: {isLandscape}, targetMatch calculated: {targetMatch}");
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
