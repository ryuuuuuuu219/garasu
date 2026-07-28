using System.IO;
using GlassShooter.Gameplay;
using PolygonRendering;
using PolygonRendering.Input;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GlassShooterPrototypeBuilder
{
    private const string ScenePath = "Assets/Scene/New Scene.unity";
    private const string MaterialPath = "Assets/Materials/NeonLine.mat";
    private const string ProjectilePath = "Assets/Prefabs/Projectile.prefab";
    private const string PlayerPath = "Assets/Prefabs/Player.prefab";

    [MenuItem("Tools/Glass Shooter/Build Prototype Scene")]
    public static void BuildPrototypeScene()
    {
        EnsureFolder("Assets", "Materials");
        EnsureFolder("Assets", "Prefabs");

        Material lineMaterial = CreateOrUpdateLineMaterial();
        Projectile projectilePrefab = CreateProjectilePrefab(lineMaterial);
        GameObject playerPrefab = CreatePlayerPrefab(lineMaterial, projectilePrefab);
        CreateScene(playerPrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Glass Shooter prototype scene and prefabs were generated.");
    }

    private static Material CreateOrUpdateLineMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");

        if (material == null)
        {
            material = new Material(shader) { name = "NeonLine" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        else if (shader != null)
        {
            material.shader = shader;
        }

        material.color = Color.white;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Projectile CreateProjectilePrefab(Material material)
    {
        GameObject root = new GameObject("Projectile");
        LineRenderer line = ConfigureLine(root, material, 0.055f, new Color(0.2f, 0.9f, 1f, 1f));

        RegularPolygonLineRenderer polygon = root.AddComponent<RegularPolygonLineRenderer>();
        polygon.VertexCount = 3;
        polygon.Size = 0.18f;
        polygon.IsPlayerSide = true;
        polygon.PointVertexVertically = true;
        polygon.color = new Color(0.2f, 0.9f, 1f, 1f);
        polygon.Rebuild();

        PolygonCollider2D collider = root.AddComponent<PolygonCollider2D>();
        collider.isTrigger = true;
        collider.points = TrianglePoints(0.18f);

        Projectile projectile = root.AddComponent<Projectile>();
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ProjectilePath);
        Object.DestroyImmediate(root);
        return prefab.GetComponent<Projectile>();
    }

    private static GameObject CreatePlayerPrefab(Material material, Projectile projectilePrefab)
    {
        GameObject root = new GameObject("Player");
        ConfigureLine(root, material, 0.09f, new Color(0.1f, 0.55f, 1f, 1f));

        RegularPolygonLineRenderer polygon = root.AddComponent<RegularPolygonLineRenderer>();
        polygon.VertexCount = 3;
        polygon.Size = 0.55f;
        polygon.IsPlayerSide = true;
        polygon.PointVertexVertically = true;
        polygon.color = new Color(0.1f, 0.55f, 1f, 1f);
        polygon.Rebuild();

        PolygonCollider2D collider = root.AddComponent<PolygonCollider2D>();
        collider.points = TrianglePoints(0.55f);

        Rigidbody2D body = root.AddComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;

        root.AddComponent<KeyboardInputState>();
        PlayerShooterController controller = root.AddComponent<PlayerShooterController>();

        GameObject firePointObject = new GameObject("FirePoint");
        firePointObject.transform.SetParent(root.transform, false);
        firePointObject.transform.localPosition = new Vector3(0f, 0.68f, 0f);

        SerializedObject serializedController = new SerializedObject(controller);
        serializedController.FindProperty("projectilePrefab").objectReferenceValue = projectilePrefab;
        serializedController.FindProperty("firePoint").objectReferenceValue = firePointObject.transform;
        serializedController.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static void CreateScene(GameObject playerPrefab)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        cameraObject.AddComponent<AudioListener>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.orthographic = true;
        camera.orthographicSize = 5f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 100f;
        cameraObject.transform.position = new Vector3(0f, 0f, -10f);

        GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
        player.transform.position = new Vector3(0f, -4f, 0f);

        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    private static LineRenderer ConfigureLine(GameObject target, Material material, float width, Color color)
    {
        LineRenderer line = target.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.widthMultiplier = width;
        line.numCapVertices = 2;
        line.numCornerVertices = 2;
        line.startColor = color;
        line.endColor = color;
        line.sortingOrder = 10;
        return line;
    }

    private static Vector2[] TrianglePoints(float radius)
    {
        return new[]
        {
            new Vector2(0f, radius),
            new Vector2(-0.8660254f * radius, -0.5f * radius),
            new Vector2(0.8660254f * radius, -0.5f * radius)
        };
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = Path.Combine(parent, child).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }
}
