using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Experiment
{
  public class SceneResetService : MonoBehaviour
  {
    [SerializeField]
    private global::MassVolumeCounter[] m_massVolumeCounters = null;

    [SerializeField]
    private global::ResetTerrain[] m_resetTerrains = null;

    [SerializeField]
    private AGXUnity.Model.DeformableTerrain[] m_fallbackTerrains = null;

    private void Awake()
    {
      ResolveReferences();
    }

    public void ResetScene()
    {
      ResolveReferences();

      var resetHandled = false;
      if ( m_massVolumeCounters != null ) {
        foreach ( var counter in m_massVolumeCounters ) {
          if ( counter == null )
            continue;

          counter.ResetMeasurements();
          resetHandled = true;
        }
      }

      if ( m_resetTerrains != null ) {
        foreach ( var resetTerrain in m_resetTerrains ) {
          if ( resetTerrain == null )
            continue;

          resetTerrain.ResetTerrainHeights();
          resetHandled = true;
        }
      }

      if ( resetHandled || m_fallbackTerrains == null )
        return;

      foreach ( var terrain in m_fallbackTerrains ) {
        if ( terrain != null )
          terrain.ResetHeights();
      }
    }

    private void ResolveReferences()
    {
      if ( m_massVolumeCounters == null || m_massVolumeCounters.Length == 0 )
        m_massVolumeCounters = FindObjectsOfType<global::MassVolumeCounter>();

      if ( m_resetTerrains == null || m_resetTerrains.Length == 0 )
        m_resetTerrains = FindObjectsOfType<global::ResetTerrain>();

      if ( m_fallbackTerrains == null || m_fallbackTerrains.Length == 0 )
        m_fallbackTerrains = FindObjectsOfType<AGXUnity.Model.DeformableTerrain>();
    }
  }
}
