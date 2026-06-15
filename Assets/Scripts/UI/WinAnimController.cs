using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;

public class WinAnimController : MonoBehaviour
{
  private enum Tier { None, TextOnly, Big, Mega, Booming }
  private enum ActiveAnim { None, Big, Mega, Booming }

  [Header("Win Panel")]
  [SerializeField] private CanvasGroup winPanelGroup;
  [SerializeField] private TMP_Text winText;

  [Header("Win Image Animations")]
  [SerializeField] private ImageAnimation bigWinAnim;
  [SerializeField] private ImageAnimation megaWinAnim;
  [SerializeField] private ImageAnimation boomingWinAnim;
  [SerializeField] private RectTransform bigWinRect;
  [SerializeField] private RectTransform megaWinRect;
  [SerializeField] private RectTransform boomingWinRect;

  [Header("Particle Pools")]
  [SerializeField] private FallingParticlePool coinPool;
  [SerializeField] private FallingParticlePool diamondPool;

  [Header("Tier Thresholds (× totalBet)")]
  [SerializeField] private float textOnlyMul = 0.5f;
  [SerializeField] private float bigMul = 5f;
  [SerializeField] private float megaMul = 10f;
  [SerializeField] private float boomingMul = 15f;

  [Header("Swap Points (× totalBet)")]
  [SerializeField] private float bigToMegaSwapAtMultiplier = 5f;
  [SerializeField] private float megaToBoomingSwapAtMultiplier = 10f;

  [Header("Timings")]
  [SerializeField] private float textOnlyLerpDuration = 1.2f;
  [SerializeField] private float bigLerpDuration = 2.5f;
  [SerializeField] private float megaLerpDuration = 3.5f;
  [SerializeField] private float boomingLerpDuration = 4.5f;
  [SerializeField] private float scaleUpDuration = 0.5f;
  [SerializeField] private float scaleDownDuration = 0.35f;
  [SerializeField] private float swapDownDuration = 0.18f;
  [SerializeField] private float swapUpDuration = 0.28f;
  [SerializeField] private float postLerpHold = 1.5f;
  [SerializeField] private float panelFadeInDuration = 0.2f;
  [SerializeField] private float textFadeOutDuration = 0.35f;

  [Header("Rain Spawn Counts")]
  [SerializeField] private int bigSpawnCount = 10;
  [SerializeField] private int megaSpawnCount = 20;
  [SerializeField] private int boomingSpawnCount = 30;
  [SerializeField] private float rainRespawnInterval = 0.6f;

  [Header("Debug")]
  [SerializeField] private bool enableDebugKeys = false;
  [SerializeField] private float debugBetAmount = 1f;

  private Coroutine _runRoutine;
  private Coroutine _rainRoutine;
  private List<Tween> _tweens = new List<Tween>();
  private bool _skip;
  private ActiveAnim _activeAnim = ActiveAnim.None;

  internal bool IsPlaying { get; private set; }
  internal System.Action<string> playAudio;
  internal System.Action<string, float> fadeAudio;
  [SerializeField] private float winAudioFadeOutDuration = 0.35f;
  private string _activeAudioKey;

  private void Awake()
  {
    if (winPanelGroup != null) winPanelGroup.alpha = 0f;
    SnapHideAll();
  }

  private void Update()
  {
    if (!enableDebugKeys) return;
    if (Input.GetKeyDown(KeyCode.Alpha1)) Trigger(debugBetAmount * 2f, debugBetAmount);
    if (Input.GetKeyDown(KeyCode.Alpha2)) Trigger(debugBetAmount * 7f, debugBetAmount);
    if (Input.GetKeyDown(KeyCode.Alpha3)) Trigger(debugBetAmount * 12f, debugBetAmount);
    if (Input.GetKeyDown(KeyCode.Alpha4)) Trigger(debugBetAmount * 20f, debugBetAmount);
    if (Input.GetKeyDown(KeyCode.Alpha5)) Skip();
  }

  internal void Trigger(double winAmount, double totalBet)
  {
    if (totalBet <= 0) return;
    Tier tier = ResolveTier(winAmount, totalBet);

    // Audio plays for any win > 0, even below the TextOnly threshold (which skips the animation).
    // Big/Mega/Booming → bigwin; everything else with a win → normalwin.
    string audioKey = tier >= Tier.Big ? "bigwin" : (winAmount > 0 ? "normalwin" : null);

    if (tier == Tier.None)
    {
      if (audioKey != null) playAudio?.Invoke(audioKey);
      return;
    }

    if (IsPlaying) Skip();

    _activeAudioKey = audioKey;
    playAudio?.Invoke(_activeAudioKey);

    if (_runRoutine != null) StopCoroutine(_runRoutine);
    _runRoutine = StartCoroutine(Run(winAmount, totalBet, tier));
  }

  internal void Skip()
  {
    if (!IsPlaying) return;
    _skip = true;
    if (!string.IsNullOrEmpty(_activeAudioKey))
    {
      fadeAudio?.Invoke(_activeAudioKey, winAudioFadeOutDuration);
      _activeAudioKey = null;
    }
  }

  internal IEnumerator WaitUntilDone()
  {
    while (IsPlaying) yield return null;
  }

  private Tier ResolveTier(double win, double bet)
  {
    if (win < bet * textOnlyMul) return Tier.None;
    if (win < bet * bigMul) return Tier.TextOnly;
    if (win < bet * megaMul) return Tier.Big;
    if (win < bet * boomingMul) return Tier.Mega;
    return Tier.Booming;
  }

  private IEnumerator Run(double winAmount, double totalBet, Tier tier)
  {
    IsPlaying = true;
    _skip = false;
    KillAllTweens();
    SnapHideAll();

    int decimals;
    if (tier == Tier.TextOnly) decimals = TextFormatter.GetSignificantDecimals(winAmount);
    else decimals = TextFormatter.GetSignificantDecimals(winAmount, 2);

    if (winText != null)
    {
      winText.text = TextFormatter.FormatSprite(0, decimals, true);
      winText.transform.localScale = Vector3.one;
      SetTextAlpha(0f);
    }

    // Panel fade-in
    if (winPanelGroup != null)
    {
      winPanelGroup.alpha = 0f;
      _tweens.Add(winPanelGroup.DOFade(1f, panelFadeInDuration));
    }
    _tweens.Add(DOTween.To(GetTextAlpha, SetTextAlpha, 1f, panelFadeInDuration));

    // Big-tier or higher: scale in big-win anim and start rain.
    if (tier >= Tier.Big)
    {
      ActivateAnim(ActiveAnim.Big);
      ScaleIn(bigWinRect);
      ScaleIn(winText != null ? winText.transform as RectTransform : null);
      _rainRoutine = StartCoroutine(RainLoop(SpawnCountForTier(tier)));
    }

    float lerpDuration = LerpDurationForTier(tier);

    if (tier == Tier.TextOnly)
    {
      // Pop final amount directly (no value lerp); the panel/text alpha fade-in handles the entrance.
      if (winText != null) winText.text = TextFormatter.FormatSprite(winAmount, decimals, true);
      float waited = 0f;
      while (waited < lerpDuration)
      {
        if (_skip) break;
        waited += Time.deltaTime;
        yield return null;
      }
    }
    else
    {
      float elapsed = 0f;
      double displayed = 0;

      while (elapsed < lerpDuration)
      {
        if (_skip) { displayed = winAmount; break; }
        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / lerpDuration);
        displayed = winAmount * t;

        if (winText != null) winText.text = TextFormatter.FormatSprite(displayed, decimals, true);

        // Swap big -> mega when displayed crosses (5 × bet) for mega/booming tiers.
        if (tier >= Tier.Mega && _activeAnim == ActiveAnim.Big
            && displayed >= totalBet * bigToMegaSwapAtMultiplier)
        {
          yield return SwapAnim(ActiveAnim.Big, ActiveAnim.Mega);
        }
        // Swap mega -> booming when displayed crosses (10 × bet) for booming tier.
        else if (tier == Tier.Booming && _activeAnim == ActiveAnim.Mega
                 && displayed >= totalBet * megaToBoomingSwapAtMultiplier)
        {
          yield return SwapAnim(ActiveAnim.Mega, ActiveAnim.Booming);
        }

        yield return null;
      }
    }

    // Final value snap + ensure correct anim for the tier.
    if (winText != null) winText.text = TextFormatter.FormatSprite(winAmount, decimals, true);

    if (!_skip)
    {
      if (tier == Tier.Mega && _activeAnim == ActiveAnim.Big)
        yield return SwapAnim(ActiveAnim.Big, ActiveAnim.Mega);
      else if (tier == Tier.Booming && _activeAnim == ActiveAnim.Big)
      {
        yield return SwapAnim(ActiveAnim.Big, ActiveAnim.Mega);
        yield return SwapAnim(ActiveAnim.Mega, ActiveAnim.Booming);
      }
      else if (tier == Tier.Booming && _activeAnim == ActiveAnim.Mega)
        yield return SwapAnim(ActiveAnim.Mega, ActiveAnim.Booming);
    }

    // Hold (skippable).
    float holdElapsed = 0f;
    while (holdElapsed < postLerpHold)
    {
      if (_skip) break;
      holdElapsed += Time.deltaTime;
      yield return null;
    }

    // Stop spawning new particles. RainLoop checks _skip between bursts but the pools' in-flight
    // SpawnBurst coroutines have their own delay timeline — stop those too so no stragglers spawn
    // while we're fading out.
    if (_rainRoutine != null) { StopCoroutine(_rainRoutine); _rainRoutine = null; }
    if (coinPool != null) coinPool.StopAllCoroutines();
    if (diamondPool != null) diamondPool.StopAllCoroutines();

    // Fade text always; scale-down text only for Big/Mega/Booming (text-only tier just pops/fades).
    if (winText != null)
    {
      _tweens.Add(DOTween.To(GetTextAlpha, SetTextAlpha, 0f, textFadeOutDuration));
      if (tier >= Tier.Big)
        _tweens.Add((winText.transform as RectTransform).DOScale(Vector3.zero, scaleDownDuration).SetEase(Ease.InBack));
    }

    RectTransform activeRect = RectFor(_activeAnim);
    if (activeRect != null)
    {
      var rect = activeRect;
      var animRef = AnimFor(_activeAnim);
      _tweens.Add(rect.DOScale(Vector3.zero, scaleDownDuration).SetEase(Ease.InBack).OnComplete(() =>
      {
        if (animRef != null)
        {
          animRef.StopAnimation();
          animRef.gameObject.SetActive(false);
        }
      }));
    }

    if (coinPool != null) coinPool.FadeOutAndReturnAll();
    if (diamondPool != null) diamondPool.FadeOutAndReturnAll();

    yield return new WaitForSeconds(Mathf.Max(scaleDownDuration, textFadeOutDuration));

    if (winPanelGroup != null)
    {
      var pg = winPanelGroup;
      _tweens.Add(pg.DOFade(0f, panelFadeInDuration).OnComplete(() => { pg.alpha = 0f; }));
    }

    yield return new WaitForSeconds(panelFadeInDuration);

    SnapHideAll();
    _activeAnim = ActiveAnim.None;
    _activeAudioKey = null;
    _skip = false;
    IsPlaying = false;
    _runRoutine = null;
  }

  private IEnumerator RainLoop(int perBurst)
  {
    while (IsPlaying && !_skip)
    {
      int coinCount = perBurst / 2;
      int diamondCount = perBurst - coinCount;

      // Kick off both bursts in parallel so coins and diamonds rain together instead of
      // coins-then-diamonds sequentially. Start via each pool's own StartCoroutine so the burst
      // is OWNED by the pool — then coinPool.StopAllCoroutines() during reset actually kills it
      // (otherwise the burst would be parented to this controller and survive the reset).
      Coroutine coinBurst = coinPool != null ? coinPool.StartCoroutine(coinPool.SpawnBurst(coinCount)) : null;
      Coroutine diamondBurst = diamondPool != null ? diamondPool.StartCoroutine(diamondPool.SpawnBurst(diamondCount)) : null;

      if (coinBurst != null) yield return coinBurst;
      if (diamondBurst != null) yield return diamondBurst;

      float waited = 0f;
      while (waited < rainRespawnInterval)
      {
        if (!IsPlaying || _skip) yield break;
        waited += Time.deltaTime;
        yield return null;
      }
    }
  }

  private IEnumerator SwapAnim(ActiveAnim from, ActiveAnim to)
  {
    RectTransform fromRect = RectFor(from);
    ImageAnimation fromAnim = AnimFor(from);
    RectTransform toRect = RectFor(to);
    ImageAnimation toAnim = AnimFor(to);

    if (fromRect != null)
    {
      yield return fromRect.DOScale(Vector3.zero, swapDownDuration).SetEase(Ease.InBack).WaitForCompletion();
      if (fromAnim != null) { fromAnim.StopAnimation(); fromAnim.gameObject.SetActive(false); }
    }

    ActivateAnim(to);
    if (toRect != null)
    {
      toRect.localScale = Vector3.zero;
      yield return toRect.DOScale(Vector3.one, swapUpDuration).SetEase(Ease.OutBack).WaitForCompletion();
    }
  }

  private void ActivateAnim(ActiveAnim which)
  {
    ImageAnimation anim = AnimFor(which);
    if (anim != null)
    {
      anim.gameObject.SetActive(true);
      anim.doLoopAnimation = true;
      anim.StopAnimation();
      anim.StartAnimation();
    }
    _activeAnim = which;
  }

  private RectTransform RectFor(ActiveAnim a)
  {
    switch (a)
    {
      case ActiveAnim.Big: return bigWinRect;
      case ActiveAnim.Mega: return megaWinRect;
      case ActiveAnim.Booming: return boomingWinRect;
      default: return null;
    }
  }

  private ImageAnimation AnimFor(ActiveAnim a)
  {
    switch (a)
    {
      case ActiveAnim.Big: return bigWinAnim;
      case ActiveAnim.Mega: return megaWinAnim;
      case ActiveAnim.Booming: return boomingWinAnim;
      default: return null;
    }
  }

  private int SpawnCountForTier(Tier t)
  {
    switch (t)
    {
      case Tier.Big: return bigSpawnCount;
      case Tier.Mega: return megaSpawnCount;
      case Tier.Booming: return boomingSpawnCount;
      default: return 0;
    }
  }

  private float LerpDurationForTier(Tier t)
  {
    switch (t)
    {
      case Tier.TextOnly: return textOnlyLerpDuration;
      case Tier.Big: return bigLerpDuration;
      case Tier.Mega: return megaLerpDuration;
      case Tier.Booming: return boomingLerpDuration;
      default: return textOnlyLerpDuration;
    }
  }

  private void ScaleIn(RectTransform rt)
  {
    if (rt == null) return;
    rt.localScale = Vector3.zero;
    _tweens.Add(rt.DOScale(Vector3.one, scaleUpDuration).SetEase(Ease.OutBack));
  }

  private void SnapHideAll()
  {
    HideAnim(bigWinAnim, bigWinRect);
    HideAnim(megaWinAnim, megaWinRect);
    HideAnim(boomingWinAnim, boomingWinRect);
    if (winText != null)
    {
      winText.transform.localScale = Vector3.one;
      SetTextAlpha(0f);
    }
  }

  private void HideAnim(ImageAnimation anim, RectTransform rect)
  {
    if (anim != null) { anim.StopAnimation(); anim.gameObject.SetActive(false); }
    if (rect != null) rect.localScale = Vector3.zero;
  }

  private float GetTextAlpha()
  {
    if (winText == null) return 0f;
    return winText.color.a;
  }

  private void SetTextAlpha(float a)
  {
    if (winText == null) return;
    Color c = winText.color;
    winText.color = new Color(c.r, c.g, c.b, a);
  }

  private void KillAllTweens()
  {
    foreach (var t in _tweens) t?.Kill();
    _tweens.Clear();
  }
}
