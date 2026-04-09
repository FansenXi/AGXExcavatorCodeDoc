using System.Collections.Generic;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Fusion
{
  public sealed class RoiTemporalSmoother
  {
    private sealed class Track
    {
      public RoiDescriptor Descriptor = null;
      public int LostFrames = 0;
    }

    private readonly List<Track> m_tracks = new List<Track>();

    public void Smooth( List<RoiDescriptor> detections,
                        RoiEncConfiguration.SmoothingOptions options,
                        List<RoiDescriptor> smoothedResults )
    {
      smoothedResults.Clear();
      options = options ?? new RoiEncConfiguration.SmoothingOptions();

      foreach ( var track in m_tracks )
        track.LostFrames += 1;

      if ( detections != null ) {
        foreach ( var detection in detections ) {
          if ( detection == null || !detection.IsValid )
            continue;

          var match = FindBestTrack( detection, options.MatchIouThreshold );
          if ( match == null ) {
            match = new Track
            {
              Descriptor = detection.Clone(),
              LostFrames = 0
            };
            m_tracks.Add( match );
          }
          else {
            match.Descriptor.NormalizedRect = RoiMathUtility.LerpRect(
              match.Descriptor.NormalizedRect,
              detection.NormalizedRect,
              options.EmaAlpha );
            match.Descriptor.Confidence = Mathf.Lerp( match.Descriptor.Confidence, detection.Confidence, options.EmaAlpha );
            match.Descriptor.Priority = detection.Priority;
            match.Descriptor.FrameId = detection.FrameId;
            match.Descriptor.Source = detection.Source;
            match.Descriptor.Category = detection.Category;
            match.Descriptor.DebugText = detection.DebugText;
            match.LostFrames = 0;
          }
        }
      }

      for ( var trackIndex = m_tracks.Count - 1; trackIndex >= 0; --trackIndex ) {
        var track = m_tracks[ trackIndex ];
        if ( track == null || track.Descriptor == null ) {
          m_tracks.RemoveAt( trackIndex );
          continue;
        }

        if ( track.LostFrames > options.LostFrameHoldCount ) {
          m_tracks.RemoveAt( trackIndex );
          continue;
        }

        smoothedResults.Add( track.Descriptor.Clone() );
      }
    }

    private Track FindBestTrack( RoiDescriptor detection, float iouThreshold )
    {
      Track bestTrack = null;
      var bestIou = iouThreshold;

      foreach ( var track in m_tracks ) {
        if ( track == null || track.Descriptor == null || track.Descriptor.Category != detection.Category )
          continue;

        var iou = RoiMathUtility.IntersectionOverUnion( track.Descriptor.NormalizedRect, detection.NormalizedRect );
        if ( iou < bestIou )
          continue;

        bestIou = iou;
        bestTrack = track;
      }

      return bestTrack;
    }
  }
}
