using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class FallingParticlePool : GenericObjectPool<FallingParticleView>
{
  [Header("Falling")]
  [SerializeField] private Transform[] spawnAnchors;
  [SerializeField] private float fallDeltaY = -1200f;
  [SerializeField] private Vector2 fallDurationRange = new Vector2(1.2f, 2.2f);
  [SerializeField] private Vector2 coinSpinSpeedRange = new Vector2(3f, 8f);
  [SerializeField] private Vector2 spawnDelayRange = new Vector2(0.05f, 0.18f);
  [SerializeField] private Vector2 startRotationRange = new Vector2(-180f, 180f);
  [SerializeField] private float fadeOutDuration = 0.35f;

  internal IEnumerator SpawnBurst(int count)
  {
    if (spawnAnchors == null || spawnAnchors.Length == 0) yield break;
    for (int i = 0; i < count; i++)
    {
      SpawnOne();
      float delay = Random.Range(spawnDelayRange.x, spawnDelayRange.y);
      yield return new WaitForSeconds(delay);
    }
  }

  private void SpawnOne()
  {
    Transform anchor = spawnAnchors[Random.Range(0, spawnAnchors.Length)];
    if (anchor == null) return;

    FallingParticleView view = GetFromPool();
    if (view == null) return;

    view.transform.SetParent(ParentTransform != null ? ParentTransform : anchor.parent, false);
    view.transform.position = anchor.position;
    view.transform.localScale = Vector3.one;

    float fallDuration = Random.Range(fallDurationRange.x, fallDurationRange.y);
    float startRotZ = Random.Range(startRotationRange.x, startRotationRange.y);
    float coinSpeed = Random.Range(coinSpinSpeedRange.x, coinSpinSpeedRange.y);

    view.Play(anchor.position, startRotZ, fallDeltaY, fallDuration, coinSpeed, () =>
    {
      if (view != null && ItemsInUse.Contains(view))
        ReturnToPool(view);
    });
  }

  internal void FadeOutAndReturnAll()
  {
    if (ItemsInUse.Count == 0) return;
    var snapshot = new List<FallingParticleView>(ItemsInUse);
    foreach (var view in snapshot)
    {
      if (view == null) continue;
      var v = view;
      v.FadeOut(fadeOutDuration, () =>
      {
        if (v != null && ItemsInUse.Contains(v))
          ReturnToPool(v);
      });
    }
  }
}
