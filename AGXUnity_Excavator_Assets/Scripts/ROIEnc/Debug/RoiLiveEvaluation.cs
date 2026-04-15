using System;
using System.Collections.Generic;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Debug
{
  [Serializable]
  public sealed class RoiClassEvaluation
  {
    public string Label = string.Empty;
    public int PredictedCount = 0;
    public int GroundTruthCount = 0;
    public int MatchedCount = 0;
    public int MissedCount = 0;
    public int FalsePositiveCount = 0;

    public RoiClassEvaluation Clone()
    {
      return (RoiClassEvaluation)MemberwiseClone();
    }
  }

  [Serializable]
  public sealed class RoiLiveEvaluation
  {
    public int UnknownCount = 0;
    public int RawCandidateCount = 0;
    public int ThresholdKeptCount = 0;
    public int NmsKeptCount = 0;
    public int FinalDetectionCount = 0;
    public float PostprocessMs = 0.0f;

    public readonly List<RoiClassEvaluation> ClassEvaluations = new List<RoiClassEvaluation>();

    public void CopyFrom( RoiLiveEvaluation other )
    {
      UnknownCount = other != null ? other.UnknownCount : 0;
      RawCandidateCount = other != null ? other.RawCandidateCount : 0;
      ThresholdKeptCount = other != null ? other.ThresholdKeptCount : 0;
      NmsKeptCount = other != null ? other.NmsKeptCount : 0;
      FinalDetectionCount = other != null ? other.FinalDetectionCount : 0;
      PostprocessMs = other != null ? other.PostprocessMs : 0.0f;

      ClassEvaluations.Clear();
      if ( other == null )
        return;

      foreach ( var classEvaluation in other.ClassEvaluations ) {
        if ( classEvaluation != null )
          ClassEvaluations.Add( classEvaluation.Clone() );
      }
    }
  }
}
