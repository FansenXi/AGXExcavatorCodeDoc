#if UNITY_EDITOR
using AGXUnity_Excavator.Scripts.Control.Sources;
using UnityEditor;
using UnityEngine;

namespace AGXUnity_Excavator.Scripts.Editor
{
  [CustomEditor( typeof( FarmStickOperatorCommandSource ) )]
  public sealed class FarmStickOperatorCommandSourceEditor : UnityEditor.Editor
  {
    private const float MinSensitivity = 0.1f;
    private const float MaxSensitivity = 2.0f;

    private SerializedProperty m_leftStickXSensitivity = null;
    private SerializedProperty m_leftStickYSensitivity = null;
    private SerializedProperty m_rightStickXSensitivity = null;
    private SerializedProperty m_rightStickYSensitivity = null;

    private void OnEnable()
    {
      m_leftStickXSensitivity = serializedObject.FindProperty( "m_leftStickXSensitivity" );
      m_leftStickYSensitivity = serializedObject.FindProperty( "m_leftStickYSensitivity" );
      m_rightStickXSensitivity = serializedObject.FindProperty( "m_rightStickXSensitivity" );
      m_rightStickYSensitivity = serializedObject.FindProperty( "m_rightStickYSensitivity" );
    }

    public override void OnInspectorGUI()
    {
      serializedObject.Update();

      DrawPropertiesExcluding(
        serializedObject,
        "m_leftStickXSensitivity",
        "m_leftStickYSensitivity",
        "m_rightStickXSensitivity",
        "m_rightStickYSensitivity",
        "m_Script" );

      EditorGUILayout.Space();
      EditorGUILayout.LabelField( "Joystick Sensitivity", EditorStyles.boldLabel );
      EditorGUILayout.HelpBox( "Usage labels follow the default ISO excavator mapping.", MessageType.None );
      DrawSensitivitySlider( m_leftStickXSensitivity, "Swing Sensitivity" );
      DrawSensitivitySlider( m_leftStickYSensitivity, "Stick Sensitivity" );
      DrawSensitivitySlider( m_rightStickXSensitivity, "Bucket Sensitivity" );
      DrawSensitivitySlider( m_rightStickYSensitivity, "Boom Sensitivity" );

      serializedObject.ApplyModifiedProperties();
    }

    private static void DrawSensitivitySlider( SerializedProperty property, string label )
    {
      if ( property == null )
        return;

      EditorGUILayout.Slider( property, MinSensitivity, MaxSensitivity, new GUIContent( label ) );
    }
  }
}
#endif
