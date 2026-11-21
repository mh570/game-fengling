using UnityEngine;

namespace Fengling.Player
{
    /// <summary>
    /// 给角色加一个"正面"标识：在角色前方放一个小方块。
    ///
    /// 为什么需要：角色是个胶囊，看不出朝向；而移动已经改成"朝移动方向转身"，
    /// 没有视觉参照就不知道转没转。
    ///
    /// 自建 SRP 下必须用 Fengling 的 shader，用内置材质会变洋红。
    /// </summary>
    [AddComponentMenu("Fengling/Player/Facing Marker")]
    public sealed class PlayerFacingMarker : MonoBehaviour
    {
        [Tooltip("标识在角色前方的距离")]
        public float forwardOffset = 0.45f;
        [Tooltip("标识高度")]
        public float heightOffset = 0.9f;
        public Color color = new Color(1f, 0.45f, 0.1f, 1f);

        private GameObject _marker;

        private void Start()
        {
            if (_marker != null) return;

            _marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _marker.name = "FacingMarker";
            Destroy(_marker.GetComponent<Collider>());

            _marker.transform.SetParent(transform, false);
            _marker.transform.localPosition = new Vector3(0f, heightOffset, forwardOffset);
            _marker.transform.localScale = new Vector3(0.18f, 0.18f, 0.4f);

            var renderer = _marker.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Shader shader = Shader.Find("Fengling/Lit");
            if (shader != null)
            {
                renderer.sharedMaterial = new Material(shader) { name = "FacingMarker (instance)" };
                renderer.sharedMaterial.SetColor("_BaseColor", color);
                renderer.sharedMaterial.SetColor("_Color", color);
                renderer.sharedMaterial.color = color;
            }
            else
            {
                Debug.LogWarning("[Facing] 找不到 Fengling/Lit，标识会显示为洋红");
            }
        }
    }
}
