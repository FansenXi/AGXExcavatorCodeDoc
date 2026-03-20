using AGXUnity;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class ExcavationMassTracker : ScriptComponent
{
  public AGXUnity.Model.DeformableTerrainShovel shovel;
  public AGXUnity.Model.DeformableTerrain m_terrain;

  float m_excavatedMass = 0;
  float m_massInBucket = 0;
  float m_previousMassInBucket = 0;

  Text m_infoText;

  // The current scene doesn't provide the particle/contact sensor that the
  // original excavated-volume pipeline depended on. Keep the excavated output
  // by accumulating positive changes in bucket load across the episode.
  public float ExcavatedMass => m_excavatedMass;
  public float MassInBucket => m_massInBucket;


  protected override bool Initialize()
  {
    var texts = GetComponentsInChildren<Text>();
    m_infoText = texts.FirstOrDefault( t => t.name == "Information" );
    ResetMeasurements();

    return base.Initialize();
  }

  public void ResetMeasurements()
  {
    m_excavatedMass = 0;
    m_massInBucket = ReadMassInBucket();
    m_previousMassInBucket = m_massInBucket;
    UpdateInfoText();
  }

  void Update()
  {
    m_massInBucket = ReadMassInBucket();
    m_excavatedMass += Mathf.Max( 0.0f, m_massInBucket - m_previousMassInBucket );
    m_previousMassInBucket = m_massInBucket;
    UpdateInfoText();
  }

  private float ReadMassInBucket()
  {
    if ( m_terrain == null || m_terrain.Native == null || shovel == null || shovel.Native == null )
      return 0.0f;

    return (float)m_terrain.Native.getDynamicMass( shovel.Native );
  }

  private void UpdateInfoText()
  {
    if ( m_infoText == null )
      return;

    string info = string.Format( "Mass in bucket: \t\t{0:f} kg\n", m_massInBucket );
    info += string.Format( "Excavated mass: \t{0:f} kg\n", m_excavatedMass );
    m_infoText.text = info;
  }
}
