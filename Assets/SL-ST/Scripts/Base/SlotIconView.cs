using UnityEngine;
using UnityEngine.UI;

public class SlotIconView : MonoBehaviour
{
  [SerializeField] internal int id = -1;
  [SerializeField] internal Image iconImage;

  internal void SetIcon(Sprite image, int ID)
  {
    iconImage.sprite = image;
    id = ID;
  }
}
