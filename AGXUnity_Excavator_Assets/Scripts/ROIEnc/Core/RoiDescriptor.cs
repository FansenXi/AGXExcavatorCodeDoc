using System;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Core
{
  [Serializable]
  public sealed class RoiDescriptor
  {
    [SerializeField]
    private RoiCategory m_category = RoiCategory.Unknown;

    [SerializeField]
    private RoiSource m_source = RoiSource.Unknown;

    [SerializeField]
    private Rect m_normalizedRect = new Rect( 0.0f, 0.0f, 0.0f, 0.0f );

    [SerializeField]
    [Range( 0.0f, 1.0f )]
    private float m_confidence = 0.0f;

    [SerializeField]
    private int m_priority = 0;

    [SerializeField]
    private long m_frameId = -1;

    [SerializeField]
    private string m_debugText = string.Empty;

    public RoiCategory Category
    {
      get => m_category;
      set => m_category = value;
    }

    public RoiSource Source
    {
      get => m_source;
      set => m_source = value;
    }

    public Rect NormalizedRect
    {
      get => m_normalizedRect;
      set => m_normalizedRect = value;
    }

    public float Confidence
    {
      get => m_confidence;
      set => m_confidence = Mathf.Clamp01( value );
    }

    public int Priority
    {
      get => m_priority;
      set => m_priority = value;
    }

    public long FrameId
    {
      get => m_frameId;
      set => m_frameId = value;
    }

    public string DebugText
    {
      get => m_debugText;
      set => m_debugText = value ?? string.Empty;
    }

    public string Label => RoiCategoryUtility.ToLabel( m_category );
    public bool IsValid => m_normalizedRect.width > 0.0f && m_normalizedRect.height > 0.0f;

    public RoiDescriptor Clone()
    {
      return new RoiDescriptor
      {
        Category = m_category,
        Source = m_source,
        NormalizedRect = m_normalizedRect,
        Confidence = m_confidence,
        Priority = m_priority,
        FrameId = m_frameId,
        DebugText = m_debugText
      };
    }
  }
}
