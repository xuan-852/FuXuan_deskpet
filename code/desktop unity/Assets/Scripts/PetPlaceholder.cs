using UnityEngine;

/// <summary>
/// 占位渲染器 — 在透明窗口方案测试阶段使用
///
/// 功能：
/// - 在宠物位置绘制一个简单的彩色方块/圆形
/// - 跟随 DesktopPet 的物理位置移动
/// - 便于验证透明窗口和物理循环
///
/// 后续会被 UnityModelRenderer 替换为真正的 Sprite 渲染
/// </summary>
[RequireComponent(typeof(DesktopPet))]
public class PetPlaceholder : MonoBehaviour
{
    private DesktopPet _pet;

    [Header("渲染设置")]
    public Color petColor = new Color(1f, 0.5f, 0f); // 橙色
    public float pixelsPerUnit = 100f;

    private GameObject _visual;

    private void Start()
    {
        _pet = GetComponent<DesktopPet>();

        // 创建一个简单的 Sprite 作为占位
        _visual = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _visual.name = "PetVisual";
        _visual.transform.SetParent(transform);

        // 设置颜色
        Renderer renderer = _visual.GetComponent<Renderer>();
        if (renderer != null)
        {
            Material mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = petColor;
            renderer.material = mat;
        }

        // 初始位置
        UpdateVisualPosition();
    }

    private void LateUpdate()
    {
        UpdateVisualPosition();
    }

    private void UpdateVisualPosition()
    {
        if (_visual == null || _pet == null)
            return;

        // 将像素坐标转为 Unity 世界坐标
        // 假设相机是正交的，1 unit = pixelsPerUnit 像素
        float worldX = _pet.petX / pixelsPerUnit;
        float worldY = _pet.petY / pixelsPerUnit;

        _visual.transform.localPosition = new Vector3(worldX, worldY, 0);

        // 缩放到宠物尺寸
        float scaleX = _pet.petWidth / pixelsPerUnit;
        float scaleY = _pet.petHeight / pixelsPerUnit;
        _visual.transform.localScale = new Vector3(scaleX, scaleY, 1);
    }
}
