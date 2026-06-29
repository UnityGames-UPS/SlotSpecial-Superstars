using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class AudioController : MonoBehaviour
{
  [Serializable]
  internal class AudioEntry
  {
    public string type;
    public string group;
    public AudioSource source;
    public AudioClip clip;
    public bool loop;
    [Range(0.1f, 3f)] public float pitch = 1f;
  }

  [SerializeField] private List<AudioEntry> entries = new List<AudioEntry>();
  // [Superstars] No background track for this game — leave empty so nothing auto-plays on Awake.
  [SerializeField] private string startupType = "";

  private readonly Dictionary<string, AudioEntry> map = new Dictionary<string, AudioEntry>();

  // Remembers the user's sound-toggle preference so focus regain doesn't override it.
  private bool userMuted;

  private void Awake()
  {
    foreach (var entry in entries)
    {
      if (string.IsNullOrEmpty(entry.type)) continue;
      map[entry.type] = entry;
    }

    if (!string.IsNullOrEmpty(startupType)) Play(startupType);
  }

  internal void Play(string type)
  {
    if (!map.TryGetValue(type, out var entry)) return;

    if (!string.IsNullOrEmpty(entry.group))
    {
      foreach (var other in entries)
      {
        if (other == entry) continue;
        if (other.group == entry.group) other.source.Stop();
      }
    }

    var source = entry.source;
    source.Stop();
    source.clip = entry.clip;
    source.loop = entry.loop;
    source.pitch = entry.pitch;
    source.Play();
  }

  internal void Stop(string type)
  {
    if (map.TryGetValue(type, out var entry)) entry.source.Stop();
  }

  internal void FadeOut(string type, float duration)
  {
    if (!map.TryGetValue(type, out var entry)) return;
    var source = entry.source;
    if (source == null || !source.isPlaying) return;
    float startVol = source.volume;
    source.DOKill();
    source.DOFade(0f, duration).OnComplete(() => { source.Stop(); source.volume = startVol; });
  }

  internal void StopAll()
  {
    foreach (var entry in entries) entry.source.Stop();
  }

  internal void SetMute(string type, bool mute)
  {
    if (map.TryGetValue(type, out var entry)) entry.source.mute = mute;
  }

  internal void SetMuteAll(bool mute)
  {
    userMuted = mute;
    foreach (var entry in entries) entry.source.mute = mute;
  }

  private void OnApplicationFocus(bool focus)
  {
    // On focus regain restore the user's preference instead of unconditionally unmuting.
    foreach (var entry in entries)
    {
      entry.source.mute = focus ? userMuted : true;
    }
  }
}
