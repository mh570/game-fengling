using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Fengling.EditorTools
{
    /// <summary>
    /// 生成场景装饰：水下石头、水草、鱼群。
    /// 石头用来验证"透过水能看见水下物体"，鱼用来验证"水下在动"。
    /// </summary>
    public static class PropSpawner
    {
        public static void SpawnAll(Transform parent, Environment.CoastTerrain terrain, float sceneWidth = 200f)
        {
            var rockMat = MakeMaterial("PropRock", new Color(0.32f, 0.32f, 0.30f), 0.15f);
            var plantMat = MakeMaterial("PropPlant", new Color(0.18f, 0.42f, 0.26f), 0.3f);
            var fishMat = MakeMaterial("PropFish", new Color(0.95f, 0.62f, 0.25f), 0.45f);

            var root = new GameObject("Props").transform;
            if (parent != null) root.SetParent(parent, false);

            var waterY = 0f;

            // ---- 石头：撒在浅水到深水
            var rockRng = new System.Random(20261118);
            float spread = sceneWidth * 0.3f;
            for (int i = 0; i < 10; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = "Rock_" + i;
                go.transform.SetParent(root, false);
                go.GetComponent<MeshFilter>().sharedMesh = RockMeshFactory.GetOrCreate();

                float x = -spread + (float)rockRng.NextDouble() * spread * 2f;
                float z = terrain.shoreZ + sceneWidth * 0.12f + (float)rockRng.NextDouble() * sceneWidth * 0.3f;
                float groundY = terrain.SampleHeight(x, z);
                float s = 0.8f + (float)rockRng.NextDouble() * 1.6f;

                go.transform.position = new Vector3(x, groundY + s * 0.25f, z);
                go.transform.localScale = new Vector3(s * 1.2f, s * 0.55f, s * 0.9f);
                go.transform.rotation = Quaternion.Euler(0f, (float)rockRng.NextDouble() * 360f, 0f);
                go.GetComponent<Renderer>().sharedMaterial = rockMat;
            }

            // ---- 水草：从海底长到水面下
            for (int i = 0; i < 14; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = "Plant_" + i;
                go.transform.SetParent(root, false);

                float x = -spread + (float)rockRng.NextDouble() * spread * 2f;
                float z = terrain.shoreZ + sceneWidth * 0.14f + (float)rockRng.NextDouble() * sceneWidth * 0.2f;
                float groundY = terrain.SampleHeight(x, z);
                float height = Mathf.Clamp(waterY - groundY - 0.3f, 0.5f, 4f);

                go.transform.position = new Vector3(x, groundY + height * 0.5f, z);
                go.transform.localScale = new Vector3(0.12f, height * 0.5f, 0.12f);
                go.GetComponent<Renderer>().sharedMaterial = plantMat;
            }

            // ---- 鱼群
            var schoolGo = new GameObject("FishSchool");
            schoolGo.transform.SetParent(root, false);
            var school = schoolGo.AddComponent<Environment.FishSchool>();

            var fishRng = new System.Random(998877);
            var list = new List<Transform>();
            for (int i = 0; i < 18; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = "Fish_" + i;
                go.transform.SetParent(schoolGo.transform, false);

                float fishSpread = sceneWidth * 0.15f;
                float x = -fishSpread + (float)fishRng.NextDouble() * fishSpread * 2f;
                float z = terrain.shoreZ + sceneWidth * 0.15f + (float)fishRng.NextDouble() * sceneWidth * 0.15f;
                float y = -1.0f - (float)fishRng.NextDouble() * 1.5f;

                go.transform.position = new Vector3(x, y, z);
                go.transform.localScale = new Vector3(0.18f, 0.28f, 0.18f);
                go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

                var col = go.GetComponent<CapsuleCollider>();
                if (col != null) Object.DestroyImmediate(col);
                go.GetComponent<Renderer>().sharedMaterial = fishMat;

                list.Add(go.transform);
            }

            school.fish = list.ToArray();
            school.waterLevel = waterY;
            school.areaCenter = new Vector2(0f, 45f);
            school.areaSize = new Vector2(70f, 40f);
        }

        private static Material MakeMaterial(string name, Color color, float smoothness)
        {
            return LitMaterialProvider.Make(name, color, smoothness);
        }
    }
}
