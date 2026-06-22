using System.Collections;
using DG.Tweening;
using UnityEngine;

// Coin-fountain pool. The base GenericObjectPool handles queue/activate/return; this subclass adds the
// continuous burst spawning and the per-coin launch randomization that gives each coin a unique arc.
public class CoinFountainPool : GenericObjectPool<CoinFountainItem>
{
  [Header("Fountain Spawn")]
  [SerializeField] private RectTransform spawnOrigin; // WinPanelBG — coins originate here (tracks its Y)
  [SerializeField] private int coinsPerBatch = 12;    // coins launched together each wave
  [SerializeField] private float batchInterval = 0.35f; // gap between waves

  [Header("Per-Coin Randomization")]
  [SerializeField] private float coinFadeInDuration = 0.15f;
  [SerializeField] private float fallToY = -800f; // anchored Y coins fall to (set to WinPanelBG hidden Y, off-screen)
  [SerializeField] private Vector2 riseRange = new Vector2(100f, 150f);
  [SerializeField] private Vector2 driftXRange = new Vector2(60f, 180f); // magnitude; sign randomized L/R
  [SerializeField] private Vector2 upDurationRange = new Vector2(0.45f, 0.7f);
  [SerializeField] private Vector2 downDurationRange = new Vector2(0.5f, 0.8f);
  [SerializeField] private Ease upEase = Ease.OutQuad;
  [SerializeField] private Ease downEase = Ease.InQuad;

  private bool _running;
  private Coroutine _loop;

  // Begins the continuous fountain. No-op if already running.
  internal void StartFountain()
  {
    if (_running) return;
    _running = true;
    _loop = StartCoroutine(SpawnLoop());
  }

  private IEnumerator SpawnLoop()
  {
    while (_running)
    {
      SpawnBatch();
      yield return new WaitForSeconds(batchInterval);
    }
  }

  private void SpawnBatch()
  {
    if (spawnOrigin == null) return;
    Vector2 origin = spawnOrigin.anchoredPosition;

    for (int i = 0; i < coinsPerBatch; i++)
    {
      CoinFountainItem coin = GetFromPool();
      if (coin == null) continue;

      float riseY = Random.Range(riseRange.x, riseRange.y);
      float driftMag = Random.Range(driftXRange.x, driftXRange.y);
      float driftX = (Random.value < 0.5f ? -1f : 1f) * driftMag;
      float upDur = Random.Range(upDurationRange.x, upDurationRange.y);
      float downDur = Random.Range(downDurationRange.x, downDurationRange.y);

      CoinFountainItem captured = coin;
      captured.Launch(origin, coinFadeInDuration, riseY, driftX, upDur, downDur, fallToY, upEase, downEase,
        () =>
        {
          // ClearAll may have run before this arc finished; only return if still in use.
          if (ItemsInUse.Contains(captured)) ReturnToPool(captured);
        });
    }
  }

  // Stops spawning new waves; leaves in-flight coins alone.
  internal void StopFountain()
  {
    _running = false;
    if (_loop != null)
    {
      StopCoroutine(_loop);
      _loop = null;
    }
  }

  // Fades out every active coin but keeps them flipping (and the fountain keeps spawning).
  internal void FadeOutAllActive(float duration)
  {
    foreach (CoinFountainItem item in ItemsInUse)
    {
      if (item != null) item.FadeOut(duration);
    }
  }

  // Stops the fountain and returns all coins to the pool.
  internal void ClearAll()
  {
    StopFountain();
    ReturnAllItemsToPool();
  }

  internal override void ReturnToPool(CoinFountainItem item)
  {
    if (item != null) item.ResetState();
    base.ReturnToPool(item);
  }

  internal override void ReturnAllItemsToPool()
  {
    foreach (CoinFountainItem item in ItemsInUse)
    {
      if (item != null) item.ResetState();
    }
    base.ReturnAllItemsToPool();
  }
}
