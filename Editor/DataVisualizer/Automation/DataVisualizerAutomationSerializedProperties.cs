namespace WallstopStudios.DataVisualizer.Editor.Automation
{
    using UnityEditor;
    using UnityEngine;

    public static partial class DataVisualizerAutomation
    {
        private static bool TryValidateSerializedProperty(
            string assetPath,
            string propertyPath,
            DataVisualizerSerializedValue value,
            out string diagnostic
        )
        {
            if (
                !TryGetSerializedProperty(
                    assetPath,
                    propertyPath,
                    out _,
                    out SerializedProperty property,
                    out diagnostic
                )
            )
            {
                return false;
            }

            return TryAssignSerializedValue(property, value, out diagnostic);
        }

        private static bool ApplySerializedProperty(
            string assetPath,
            string propertyPath,
            DataVisualizerSerializedValue value,
            out string diagnostic
        )
        {
            if (
                !TryGetSerializedProperty(
                    assetPath,
                    propertyPath,
                    out SerializedObject serializedObject,
                    out SerializedProperty property,
                    out diagnostic
                )
            )
            {
                return false;
            }

            Undo.RecordObject(
                serializedObject.targetObject,
                $"Set serialized property {propertyPath}"
            );
            if (!TryAssignSerializedValue(property, value, out diagnostic))
            {
                return false;
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(serializedObject.targetObject);
            return true;
        }

        private static bool TryGetSerializedProperty(
            string assetPath,
            string propertyPath,
            out SerializedObject serializedObject,
            out SerializedProperty property,
            out string diagnostic
        )
        {
            serializedObject = null;
            property = null;
            if (string.IsNullOrWhiteSpace(propertyPath))
            {
                diagnostic = "propertyPath is required for serialized-property operations.";
                return false;
            }

            ScriptableObject asset =
                AssetDatabase.LoadMainAssetAtPath(assetPath) as ScriptableObject;
            if (asset == null)
            {
                diagnostic = "The requested ScriptableObject asset was not found.";
                return false;
            }

            serializedObject = new SerializedObject(asset);
            property = serializedObject.FindProperty(propertyPath);
            if (property == null)
            {
                diagnostic = $"Serialized property '{propertyPath}' was not found.";
                return false;
            }

            diagnostic = string.Empty;
            return true;
        }

        private static bool TryAssignSerializedValue(
            SerializedProperty property,
            DataVisualizerSerializedValue value,
            out string diagnostic
        )
        {
            if (value == null)
            {
                diagnostic = "serializedValue is required for serialized-property operations.";
                return false;
            }

            switch (value.kind)
            {
                case DataVisualizerSerializedValueKind.String:
                    if (!RequireType(property, SerializedPropertyType.String, out diagnostic))
                    {
                        return false;
                    }
                    property.stringValue = value.stringValue ?? string.Empty;
                    return true;

                case DataVisualizerSerializedValueKind.Boolean:
                    if (!RequireType(property, SerializedPropertyType.Boolean, out diagnostic))
                    {
                        return false;
                    }
                    property.boolValue = value.boolValue;
                    return true;

                case DataVisualizerSerializedValueKind.Integer:
                    if (!RequireType(property, SerializedPropertyType.Integer, out diagnostic))
                    {
                        return false;
                    }
                    property.intValue = value.intValue;
                    return true;

                case DataVisualizerSerializedValueKind.Long:
                    if (!RequireType(property, SerializedPropertyType.Integer, out diagnostic))
                    {
                        return false;
                    }
                    property.longValue = value.longValue;
                    return true;

                case DataVisualizerSerializedValueKind.Float:
                    if (!RequireType(property, SerializedPropertyType.Float, out diagnostic))
                    {
                        return false;
                    }
                    property.floatValue = value.floatValue;
                    return true;

                case DataVisualizerSerializedValueKind.Double:
                    if (!RequireType(property, SerializedPropertyType.Float, out diagnostic))
                    {
                        return false;
                    }
                    property.doubleValue = value.doubleValue;
                    return true;

                case DataVisualizerSerializedValueKind.Enum:
                    if (!RequireType(property, SerializedPropertyType.Enum, out diagnostic))
                    {
                        return false;
                    }
                    if (value.intValue < 0 || value.intValue >= property.enumDisplayNames.Length)
                    {
                        diagnostic =
                            "Enum value is outside the serialized property's declared values.";
                        return false;
                    }
                    property.enumValueIndex = value.intValue;
                    return true;

                case DataVisualizerSerializedValueKind.ObjectReferenceGuid:
                    if (
                        !RequireType(
                            property,
                            SerializedPropertyType.ObjectReference,
                            out diagnostic
                        )
                    )
                    {
                        return false;
                    }
                    if (string.IsNullOrWhiteSpace(value.objectReferenceGuid))
                    {
                        property.objectReferenceValue = null;
                        return true;
                    }
                    string referencePath = AssetDatabase.GUIDToAssetPath(value.objectReferenceGuid);
                    UnityEngine.Object reference = string.IsNullOrWhiteSpace(referencePath)
                        ? null
                        : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(referencePath);
                    if (reference == null)
                    {
                        diagnostic = "The object-reference GUID was not found.";
                        return false;
                    }
                    property.objectReferenceValue = reference;
                    return true;

                case DataVisualizerSerializedValueKind.Color:
                    if (!RequireType(property, SerializedPropertyType.Color, out diagnostic))
                    {
                        return false;
                    }
                    property.colorValue = value.colorValue;
                    return true;

                case DataVisualizerSerializedValueKind.Vector2:
                    if (!RequireType(property, SerializedPropertyType.Vector2, out diagnostic))
                    {
                        return false;
                    }
                    property.vector2Value = value.vector2Value;
                    return true;

                case DataVisualizerSerializedValueKind.Vector3:
                    if (!RequireType(property, SerializedPropertyType.Vector3, out diagnostic))
                    {
                        return false;
                    }
                    property.vector3Value = value.vector3Value;
                    return true;

                case DataVisualizerSerializedValueKind.Vector4:
                    if (!RequireType(property, SerializedPropertyType.Vector4, out diagnostic))
                    {
                        return false;
                    }
                    property.vector4Value = value.vector4Value;
                    return true;

                case DataVisualizerSerializedValueKind.Quaternion:
                    if (!RequireType(property, SerializedPropertyType.Quaternion, out diagnostic))
                    {
                        return false;
                    }
                    property.quaternionValue = value.quaternionValue;
                    return true;

                case DataVisualizerSerializedValueKind.Rect:
                    if (!RequireType(property, SerializedPropertyType.Rect, out diagnostic))
                    {
                        return false;
                    }
                    property.rectValue = value.rectValue;
                    return true;

                case DataVisualizerSerializedValueKind.Bounds:
                    if (!RequireType(property, SerializedPropertyType.Bounds, out diagnostic))
                    {
                        return false;
                    }
                    property.boundsValue = value.boundsValue;
                    return true;

                case DataVisualizerSerializedValueKind.Vector2Int:
                    if (!RequireType(property, SerializedPropertyType.Vector2Int, out diagnostic))
                    {
                        return false;
                    }
                    property.vector2IntValue = value.vector2IntValue;
                    return true;

                case DataVisualizerSerializedValueKind.Vector3Int:
                    if (!RequireType(property, SerializedPropertyType.Vector3Int, out diagnostic))
                    {
                        return false;
                    }
                    property.vector3IntValue = value.vector3IntValue;
                    return true;

                case DataVisualizerSerializedValueKind.RectInt:
                    if (!RequireType(property, SerializedPropertyType.RectInt, out diagnostic))
                    {
                        return false;
                    }
                    property.rectIntValue = value.rectIntValue;
                    return true;

                case DataVisualizerSerializedValueKind.BoundsInt:
                    if (!RequireType(property, SerializedPropertyType.BoundsInt, out diagnostic))
                    {
                        return false;
                    }
                    property.boundsIntValue = value.boundsIntValue;
                    return true;

                case DataVisualizerSerializedValueKind.ArraySize:
                    if (!RequireType(property, SerializedPropertyType.ArraySize, out diagnostic))
                    {
                        return false;
                    }
                    if (value.intValue < 0)
                    {
                        diagnostic = "Array size cannot be negative.";
                        return false;
                    }
                    property.arraySize = value.intValue;
                    return true;

                default:
                    diagnostic = "The serialized value kind is unsupported.";
                    return false;
            }
        }

        private static bool RequireType(
            SerializedProperty property,
            SerializedPropertyType expected,
            out string diagnostic
        )
        {
            if (property.propertyType != expected)
            {
                diagnostic =
                    $"Serialized property '{property.propertyPath}' is {property.propertyType}, expected {expected}.";
                return false;
            }

            diagnostic = string.Empty;
            return true;
        }
    }
}
