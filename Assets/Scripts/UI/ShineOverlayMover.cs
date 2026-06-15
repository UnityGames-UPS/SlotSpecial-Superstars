using DG.Tweening;
using UnityEngine;

internal class ShineOverlayMover : MonoBehaviour
{
    [SerializeField] private GameObject shinePrefab;
    [SerializeField] private RectTransform parentRect;
    [SerializeField] private RectTransform startPoint;
    [SerializeField] private RectTransform endPoint;
    [SerializeField, Range(0.1f, 0.5f)] private float threshold = 0.2f;
    [SerializeField] private float duration = 2f;
    [SerializeField] private float delay = 0f;
    [SerializeField] private float loopInterval = 0f;
    [SerializeField] private bool loop = true;
    [SerializeField] private bool playOnEnable = true;

    private RectTransform shineRect;
    private CanvasGroup shineCanvasGroup;
    private Sequence sequence;

    private void Awake()
    {
        if (shinePrefab == null) return;

        Transform parent = parentRect != null ? parentRect : transform;
        GameObject instance = Instantiate(shinePrefab, parent, false);
        shineRect = instance.GetComponent<RectTransform>();
        shineCanvasGroup = instance.GetComponent<CanvasGroup>();
        if (shineCanvasGroup == null) shineCanvasGroup = instance.AddComponent<CanvasGroup>();

        shineRect.anchoredPosition = GetStartPosition();
        shineRect.localScale = Vector3.zero;
        shineCanvasGroup.alpha = 0f;
    }

    private Vector2 GetStartPosition()
    {
        return startPoint != null ? startPoint.anchoredPosition : Vector2.zero;
    }

    private Vector2 GetEndPosition()
    {
        return endPoint != null ? endPoint.anchoredPosition : Vector2.zero;
    }

    private void OnEnable()
    {
        if (playOnEnable) Play();
    }

    private void OnDisable()
    {
        Stop();
    }

    internal void Play()
    {
        if (shineRect == null) return;
        Stop();
        sequence = BuildSequence();
    }

    internal void Stop()
    {
        if (sequence != null)
        {
            sequence.Kill();
            sequence = null;
        }
    }

    private Sequence BuildSequence()
    {
        float inEnd = duration * threshold;
        float outStart = duration * (1f - threshold);
        float outDuration = duration - outStart;

        shineRect.anchoredPosition = GetStartPosition();
        shineRect.localScale = Vector3.zero;
        shineCanvasGroup.alpha = 0f;

        Sequence seq = DOTween.Sequence();
        if (delay > 0f) seq.AppendInterval(delay);

        float seqStart = seq.Duration();

        seq.Insert(seqStart, shineRect.DOAnchorPos(GetEndPosition(), duration).SetEase(Ease.Linear));

        seq.Insert(seqStart, shineRect.DOScale(1f, inEnd).SetEase(Ease.OutQuad));
        seq.Insert(seqStart, shineCanvasGroup.DOFade(1f, inEnd).SetEase(Ease.OutQuad));

        seq.Insert(seqStart + outStart, shineRect.DOScale(0f, outDuration).SetEase(Ease.InQuad));
        seq.Insert(seqStart + outStart, shineCanvasGroup.DOFade(0f, outDuration).SetEase(Ease.InQuad));

        if (loop)
        {
            if (loopInterval > 0f) seq.AppendInterval(loopInterval);
            seq.SetLoops(-1, LoopType.Restart);
        }

        return seq;
    }
}
