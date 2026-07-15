using UnityEditor;
using UnityEngine;

namespace ProjectTools.Editor
{
    [InitializeOnLoad]
    public static class SolutionGenerator
    {
        static SolutionGenerator()
        {
            EditorApplication.delayCall += Generate;
        }

        [MenuItem("Tools/Generate C# Solution")]
        public static void Generate()
        {
            Unity.CodeEditor.CodeEditor.Editor.CurrentCodeEditor.SyncAll();
            Debug.Log("C# solution and project files generated.");
        }
    }
}
