using System.Collections.Generic;
using AGXUnity_Excavator.Scripts.ROIEnc.Core;

namespace AGXUnity_Excavator.Scripts.ROIEnc.Training
{
  public sealed class SemanticLabelExporter
  {
    private readonly SceneGraphLabelGenerator m_labelGenerator;
    private readonly DatasetWriter m_datasetWriter;
    private readonly List<RoiDescriptor> m_labelBuffer = new List<RoiDescriptor>();

    public SemanticLabelExporter( SceneGraphLabelGenerator labelGenerator, DatasetWriter datasetWriter )
    {
      m_labelGenerator = labelGenerator;
      m_datasetWriter = datasetWriter;
    }

    public int CurrentEpisodeIndex => m_datasetWriter != null ? m_datasetWriter.CurrentEpisodeIndex : 0;

    public void AdvanceEpisode( RoiEncConfiguration.DatasetOptions options = null )
    {
      m_datasetWriter?.AdvanceEpisode( options );
    }

    public bool TryWriteEpisodeManifest( RoiEncConfiguration.DatasetOptions options,
                                         string captureMode,
                                         string[] classLabels,
                                         out string manifestPath,
                                         out string error )
    {
      manifestPath = string.Empty;
      error = string.Empty;

      if ( m_datasetWriter == null ) {
        error = "semantic_label_exporter_dataset_writer_missing";
        return false;
      }

      return m_datasetWriter.TryWriteEpisodeManifest( options, captureMode, classLabels, out manifestPath, out error );
    }

    public bool TryExport( RoiFrameSample frameSample,
                           RoiEncConfiguration.DatasetOptions options,
                           out string imagePath,
                           out string labelPath,
                           out string error )
    {
      imagePath = string.Empty;
      labelPath = string.Empty;
      error = string.Empty;

      if ( m_labelGenerator == null || m_datasetWriter == null ) {
        error = "semantic_label_exporter_not_initialized";
        return false;
      }

      if ( !m_labelGenerator.TryGetRois( frameSample, m_labelBuffer, out error ) )
        return false;

      return m_datasetWriter.WriteSample( frameSample, m_labelBuffer, options, out imagePath, out labelPath, out error );
    }
  }
}
