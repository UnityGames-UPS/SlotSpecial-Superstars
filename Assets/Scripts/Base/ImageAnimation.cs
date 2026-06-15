using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class ImageAnimation : MonoBehaviour
{
  public enum ImageState
  {
    NONE,
    PLAYING,
    PAUSED
  }

  public List<Sprite> textureArray;

  [SerializeField] internal Image rendererDelegate;

  public bool useSharedMaterial = true;

  public bool doLoopAnimation = true;

  [HideInInspector]
  public ImageState currentAnimationState;

  private int indexOfTexture;

  private float idealFrameRate = 0.0416666679f;

  private float delayBetweenAnimation;

  public float AnimationSpeed = 5f;

  public float delayBetweenLoop;

  [SerializeField] private float StartOnAwakeDelay;
  public bool StartOnAwake = false;

  public bool DestroyOnCompletion = false;

  [SerializeField] internal bool isplaying;

  [SerializeField]
  private Sprite OriginalSprite;

  [Header("Optional Features")]
  [SerializeField] internal bool startInvisible = false;
  [SerializeField] internal bool holdAtLastFrame = false;
  [SerializeField] internal List<Sprite> endTextureArray;

  private Tween _alphaTween;
  private List<Sprite> _originalTextureArray;
  private bool _originalDoLoop;

  void OnValidate()
  {
    if(rendererDelegate == null)
    {
      rendererDelegate = GetComponent<Image>();
    } 
  }

  private void Awake()
  {
    if (rendererDelegate == null)
    {
      rendererDelegate = GetComponent<Image>();
    }
    OriginalSprite = rendererDelegate.sprite;
    _originalTextureArray = textureArray != null ? new List<Sprite>(textureArray) : null;
    _originalDoLoop = doLoopAnimation;
    if (startInvisible)
    {
      Color c = rendererDelegate.color;
      rendererDelegate.color = new Color(c.r, c.g, c.b, 0f);
    }
  }

  private void Start()
  {
  }

  private void OnEnable()
  {
    if (StartOnAwake)
    {
      Invoke(nameof(StartAnimation), StartOnAwakeDelay);
    }
  }

  private void OnDisable()
  {
    //rendererDelegate.sprite = textureArray[0];
    StopAnimation();
  }

  private void AnimationProcess()
  {
    isplaying = true;
    SetTextureOfIndex();
    indexOfTexture++;
    if (indexOfTexture == textureArray.Count)
    {
      indexOfTexture = 0;
      if (doLoopAnimation)
      {
        Invoke("AnimationProcess", delayBetweenAnimation + delayBetweenLoop);
        isplaying = true;
      }
      else
      {
        if (holdAtLastFrame)
        {
          rendererDelegate.sprite = textureArray[textureArray.Count - 1];
          isplaying = false;
          currentAnimationState = ImageState.NONE;
        }
        else
        {
          if (DestroyOnCompletion)
          {
            this.gameObject.SetActive(false);
          }
          isplaying = false;
        }
      }
    }
    else
    {
      Invoke("AnimationProcess", delayBetweenAnimation);
      isplaying = true;

    }
  }

  public void StartAnimation()
  {
    indexOfTexture = 0;
    if (currentAnimationState == ImageState.NONE)
    {
      RevertToInitialState();
      delayBetweenAnimation = idealFrameRate * (float)textureArray.Count / AnimationSpeed;
      currentAnimationState = ImageState.PLAYING;
      Invoke("AnimationProcess", delayBetweenAnimation);
    }
  }

  public void PauseAnimation()
  {
    if (currentAnimationState == ImageState.PLAYING)
    {
      CancelInvoke("AnimationProcess");
      currentAnimationState = ImageState.PAUSED;
    }
  }

  public void ResumeAnimation()
  {
    if (currentAnimationState == ImageState.PAUSED && !IsInvoking("AnimationProcess"))
    {
      Invoke("AnimationProcess", delayBetweenAnimation);
      currentAnimationState = ImageState.PLAYING;
    }
  }

  public void StopAnimation()
  {
    if (currentAnimationState != 0)
    {
      if (!holdAtLastFrame)
      {
        try
        {
          if (OriginalSprite != null)
            rendererDelegate.sprite = OriginalSprite;
          else
            rendererDelegate.sprite = textureArray[0];
        }
        catch (System.Exception) { }
      }
      try
      {
        CancelInvoke("AnimationProcess");
      }
      catch (System.Exception) { }
      currentAnimationState = ImageState.NONE;
      isplaying = false;
    }
  }

  public void RevertToInitialState()
  {
    indexOfTexture = 0;
    SetTextureOfIndex();
  }

  internal void SetAlpha(float a)
  {
    if (rendererDelegate == null) return;
    _alphaTween?.Kill();
    Color c = rendererDelegate.color;
    rendererDelegate.color = new Color(c.r, c.g, c.b, a);
  }

  internal Tween FadeAlpha(float target, float duration)
  {
    if (rendererDelegate == null) return null;
    _alphaTween?.Kill();
    _alphaTween = rendererDelegate.DOFade(target, duration);
    return _alphaTween;
  }

  internal void ResetToFirstFrame()
  {
    indexOfTexture = 0;
    if (_originalTextureArray != null) textureArray = _originalTextureArray;
    doLoopAnimation = _originalDoLoop;
    if (rendererDelegate == null) return;
    if (OriginalSprite != null)
      rendererDelegate.sprite = OriginalSprite;
    else if (textureArray != null && textureArray.Count > 0)
      rendererDelegate.sprite = textureArray[0];
  }

  internal float Progress
  {
    get
    {
      if (textureArray == null || textureArray.Count == 0) return 0f;
      return Mathf.Clamp01((float)indexOfTexture / textureArray.Count);
    }
  }

  internal float GetEndSequenceDuration()
  {
    if (endTextureArray == null || endTextureArray.Count == 0) return 0f;
    float delay = idealFrameRate * endTextureArray.Count / AnimationSpeed;
    return delay * endTextureArray.Count;
  }

  internal void PlaySequence(List<Sprite> sprites, bool loop = false)
  {
    if (sprites == null || sprites.Count == 0) return;
    CancelInvoke(nameof(AnimationProcess));
    textureArray = sprites;
    indexOfTexture = 0;
    doLoopAnimation = loop;
    delayBetweenAnimation = idealFrameRate * (float)textureArray.Count / AnimationSpeed;
    currentAnimationState = ImageState.PLAYING;
    isplaying = true;
    if (rendererDelegate != null) rendererDelegate.sprite = textureArray[0];
    Invoke(nameof(AnimationProcess), delayBetweenAnimation);
  }

  internal void PlayEndSequence()
  {
    if (endTextureArray == null || endTextureArray.Count == 0) return;
    CancelInvoke(nameof(AnimationProcess));
    textureArray = endTextureArray;
    indexOfTexture = 0;
    doLoopAnimation = false;
    delayBetweenAnimation = idealFrameRate * (float)textureArray.Count / AnimationSpeed;
    currentAnimationState = ImageState.PLAYING;
    isplaying = true;
    Invoke(nameof(AnimationProcess), delayBetweenAnimation);
  }

  private void SetTextureOfIndex()
  {
    if (useSharedMaterial)
    {
      rendererDelegate.sprite = textureArray[indexOfTexture];
    }
    else
    {
      rendererDelegate.sprite = textureArray[indexOfTexture];
    }
  }
}
