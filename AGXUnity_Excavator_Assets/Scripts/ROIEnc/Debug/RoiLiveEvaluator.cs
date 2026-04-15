using System.Collections.Generic;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Debug
{
  public static class RoiLiveEvaluator
  {
    public static void Evaluate( IReadOnlyList<RoiDescriptor> predicted,
                                 IReadOnlyList<RoiDescriptor> groundTruth,
                                 int rawCandidates,
                                 int thresholdKept,
                                 int nmsKept,
                                 float postprocessMs,
                                 RoiLiveEvaluation evaluation,
                                 float iouThreshold = 0.5f )
    {
      if ( evaluation == null )
        return;

      evaluation.UnknownCount = 0;
      evaluation.RawCandidateCount = Mathf.Max( 0, rawCandidates );
      evaluation.ThresholdKeptCount = Mathf.Max( 0, thresholdKept );
      evaluation.NmsKeptCount = Mathf.Max( 0, nmsKept );
      evaluation.FinalDetectionCount = predicted != null ? predicted.Count : 0;
      evaluation.PostprocessMs = Mathf.Max( 0.0f, postprocessMs );
      evaluation.ClassEvaluations.Clear();

      var categories = new[]
      {
        RoiCategory.Bucket,
        RoiCategory.ExcavatorArm,
        RoiCategory.Truck,
        RoiCategory.Container,
        RoiCategory.DigArea
      };

      if ( predicted != null ) {
        foreach ( var roi in predicted ) {
          if ( roi != null && roi.Category == RoiCategory.Unknown )
            evaluation.UnknownCount += 1;
        }
      }

      foreach ( var category in categories ) {
        var predictedForCategory = FilterByCategory( predicted, category );
        var truthForCategory = FilterByCategory( groundTruth, category );
        var matchedTruth = new bool[ truthForCategory.Count ];
        var matchedCount = 0;
        var falsePositives = 0;

        predictedForCategory.Sort( CompareByScoreDescending );
        foreach ( var candidate in predictedForCategory ) {
          var bestTruthIndex = -1;
          var bestIou = 0.0f;
          for ( var truthIndex = 0; truthIndex < truthForCategory.Count; ++truthIndex ) {
            if ( matchedTruth[ truthIndex ] )
              continue;

            var iou = RoiMathUtility.IntersectionOverUnion( candidate.NormalizedRect, truthForCategory[ truthIndex ].NormalizedRect );
            if ( iou >= iouThreshold && iou > bestIou ) {
              bestIou = iou;
              bestTruthIndex = truthIndex;
            }
          }

          if ( bestTruthIndex >= 0 ) {
            matchedTruth[ bestTruthIndex ] = true;
            matchedCount += 1;
          }
          else {
            falsePositives += 1;
          }
        }

        var missed = truthForCategory.Count - matchedCount;
        if ( predictedForCategory.Count == 0 && truthForCategory.Count == 0 )
          continue;

        evaluation.ClassEvaluations.Add( new RoiClassEvaluation
        {
          Label = RoiCategoryUtility.ToLabel( category ),
          PredictedCount = predictedForCategory.Count,
          GroundTruthCount = truthForCategory.Count,
          MatchedCount = matchedCount,
          MissedCount = Mathf.Max( 0, missed ),
          FalsePositiveCount = falsePositives
        } );
      }
    }

    private static List<RoiDescriptor> FilterByCategory( IReadOnlyList<RoiDescriptor> rois, RoiCategory category )
    {
      var filtered = new List<RoiDescriptor>();
      if ( rois == null )
        return filtered;

      foreach ( var roi in rois ) {
        if ( roi != null && roi.Category == category )
          filtered.Add( roi );
      }

      return filtered;
    }

    private static int CompareByScoreDescending( RoiDescriptor left, RoiDescriptor right )
    {
      if ( ReferenceEquals( left, right ) )
        return 0;

      if ( left == null )
        return 1;

      if ( right == null )
        return -1;

      return right.Confidence.CompareTo( left.Confidence );
    }
  }
}
