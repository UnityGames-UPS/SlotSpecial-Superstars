using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SlotIconView : MonoBehaviour
{
  [SerializeField] internal int id = -1;
  [SerializeField] internal Image iconImage;

  private Transform _restParent;
  private int _restSiblingIndex;
  private Vector3 _restLocalPosition;
  
  // Lift/Drop pair: captured at Lift() time, cleared by Drop(). Null _cachedParent means
  // "not currently lifted" — so a re-Lift while already lifted won't overwrite the snapshot
  // with the overlay parent.
  private Transform _cachedParent;
  private int _cachedSiblingIndex;
  private Vector3 _cachedLocalPosition;

  void Awake()
  {
    _restParent = transform.parent;
    _restSiblingIndex = transform.GetSiblingIndex();
    _restLocalPosition = transform.localPosition;
  }

  internal void Lift(Transform overlayParent)
  {
    if (overlayParent == null) return;
    if (transform.parent == overlayParent) return; // already lifted; don't recache
    // Snapshot the pre-lift state so Drop() restores to exactly where we were, regardless
    // of any layout drift since Awake.
    if (_cachedParent == null)
    {
      _cachedParent = transform.parent;
      _cachedSiblingIndex = transform.GetSiblingIndex();
      _cachedLocalPosition = transform.localPosition;
    }
    transform.SetParent(overlayParent, worldPositionStays: true);
  }

  internal void Drop()
  {
    if (_cachedParent != null)
    {
      transform.SetParent(_cachedParent, worldPositionStays: false);
      transform.SetSiblingIndex(_cachedSiblingIndex);
      transform.localPosition = _cachedLocalPosition;
      _cachedParent = null;
      return;
    }
    // No lift cache but the icon isn't where it belongs (e.g., previous lift skipped caching
    // due to a re-entrancy guard). Fall back to the Awake-time rest snapshot rather than
    // leaving the symbol stranded.
    if (_restParent != null && transform.parent != _restParent)
      ForceRestoreToRest();
  }

  // Unconditionally snap back to the Awake-captured rest state. Used by Reset/StopAnim during
  // spin-start so a symbol stranded on the wrong parent (e.g., animationOverlayParent because a
  // mid-pulse coroutine was killed before Drop could run) is always brought home.
  private void ForceRestoreToRest()
  {
    if (_restParent == null) return;
    if (transform.parent != _restParent)
      transform.SetParent(_restParent, worldPositionStays: false);
    transform.SetSiblingIndex(_restSiblingIndex);
    transform.localPosition = _restLocalPosition;
    _cachedParent = null;
  }

  internal void SetIcon(Sprite image, int ID)
  {
    iconImage.sprite = image;
    id = ID;
  }

  internal void Reset()
  {
    ForceRestoreToRest();
  }
}
