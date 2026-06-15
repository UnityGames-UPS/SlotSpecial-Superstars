using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Procedural "sparkle" layer that replaces a pre-rendered bg frame animation. Sits on
// BGAnimationBalls and walks its descendant Images (the YellowBalls under each Column).
// All dots start fully transparent on Awake; an Update loop ticks a small active-fades
// list so the cost scales with maxConcurrent, not the ~2500-dot grid size.
public class BackgroundDotsField : MonoBehaviour
{
  [Header("Spawn")]
  [SerializeField] private float spawnIntervalMin = 0.02f;
  [SerializeField] private float spawnIntervalMax = 0.08f;
  [SerializeField] private int maxConcurrent = 20;

  [Header("Envelope (per dot)")]
  [SerializeField] private float fadeInDuration = 0.35f;
  [SerializeField] private float holdDuration = 0.0f;
  [SerializeField] private float fadeOutDuration = 0.45f;

  [Header("Color")]
  [SerializeField] private Color dotColor = Color.yellow;

  private CanvasRenderer[] _renderers;
  private float _nextSpawnTimer;

  private struct ActiveFade
  {
    public int index;
    public float t;
  }

  private readonly List<ActiveFade> _active = new List<ActiveFade>(64);
  private readonly HashSet<int> _activeSet = new HashSet<int>();

  void Awake()
  {
    // Gather every YellowBalls Image across all Column children in one shot. Two levels deep
    // (BGAnimationBalls -> Column -> YellowBalls), so we walk descendants, not direct children.
    var images = GetComponentsInChildren<Image>(includeInactive: true);
    _renderers = new CanvasRenderer[images.Length];
    var zero = new Color(dotColor.r, dotColor.g, dotColor.b, 0f);
    for (int i = 0; i < images.Length; i++)
    {
      // 2500 raycast targets would hit-test every pointer event — kill them.
      images[i].raycastTarget = false;
      _renderers[i] = images[i].canvasRenderer;
      // SetColor skips the UGUI dirty path that Image.color triggers, so it stays cheap at scale.
      _renderers[i].SetColor(zero);
    }

    _nextSpawnTimer = Random.Range(spawnIntervalMin, spawnIntervalMax);
  }

  void Update()
  {
    int total = _renderers != null ? _renderers.Length : 0;
    if (total == 0) return;

    _nextSpawnTimer -= Time.unscaledDeltaTime;
    if (_nextSpawnTimer <= 0f && _active.Count < maxConcurrent)
    {
      // Pick a random dot not currently fading. With ~2500 dots and ~20 active, collisions are
      // rare, so a few retries are cheaper than building a fresh free-list every frame.
      int idx = -1;
      for (int attempt = 0; attempt < 6; attempt++)
      {
        int candidate = Random.Range(0, total);
        if (!_activeSet.Contains(candidate)) { idx = candidate; break; }
      }
      if (idx >= 0)
      {
        _active.Add(new ActiveFade { index = idx, t = 0f });
        _activeSet.Add(idx);
      }
      _nextSpawnTimer = Random.Range(spawnIntervalMin, spawnIntervalMax);
    }

    float totalDur = fadeInDuration + holdDuration + fadeOutDuration;
    float dt = Time.unscaledDeltaTime;
    for (int i = _active.Count - 1; i >= 0; i--)
    {
      var f = _active[i];
      f.t += dt;

      float alpha;
      if (f.t < fadeInDuration)
        alpha = fadeInDuration > 0f ? f.t / fadeInDuration : 1f;
      else if (f.t < fadeInDuration + holdDuration)
        alpha = 1f;
      else
        alpha = fadeOutDuration > 0f ? 1f - (f.t - fadeInDuration - holdDuration) / fadeOutDuration : 0f;

      if (alpha < 0f) alpha = 0f;
      else if (alpha > 1f) alpha = 1f;

      _renderers[f.index].SetColor(new Color(dotColor.r, dotColor.g, dotColor.b, alpha));

      if (f.t >= totalDur)
      {
        _activeSet.Remove(f.index);
        int last = _active.Count - 1;
        _active[i] = _active[last];
        _active.RemoveAt(last);
      }
      else
      {
        _active[i] = f;
      }
    }
  }

  // Called by FreeSpinController when the normal-bg crossfade hides the dots layer. Toggling
  // enabled stops Update() entirely; whatever sparkles are mid-flight freeze in place, which
  // is invisible behind the FS bg.
  internal void Pause() => enabled = false;
  internal void Resume() => enabled = true;

  internal void SnapAllToZero()
  {
    if (_renderers == null) return;
    var zero = new Color(dotColor.r, dotColor.g, dotColor.b, 0f);
    for (int i = 0; i < _renderers.Length; i++) _renderers[i].SetColor(zero);
    _active.Clear();
    _activeSet.Clear();
  }
}
