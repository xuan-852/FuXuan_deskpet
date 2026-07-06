import json, os, datetime

# GLM-4V 扫描结果数据
vision_scan_results = [
    {"paramId":"Param401","min":0.00,"max":1.00,"suggestedSemantic":"special_outer_mask","confidence":"低","description":"部位: 特效（外蒙版）\n变化: 无明显视觉变化"},
    {"paramId":"Param421","min":0.00,"max":1.00,"suggestedSemantic":"special_outer_purple_ring_rotation","confidence":"低","description":"部位: 外紫环\n变化: 两张图中外紫环视觉效果无明显差异"},
    {"paramId":"Param431","min":0.00,"max":1.00,"suggestedSemantic":"outer_purple_ring_visibility","confidence":"高","description":"部位: 外紫环\n变化: 外紫环从隐藏状态变为显示状态，在角色身体周围出现紫色环状特效"},
    {"paramId":"Param441","min":0.00,"max":1.00,"suggestedSemantic":"special_purple_ring_outer_size","confidence":"低","description":"部位: 外紫环（特效）\n变化: 外紫环大小在最小值和最大值时视觉差异极小，几乎难以察觉"},
    {"paramId":"Param411","min":0.00,"max":1.00,"suggestedSemantic":"special_middle_mask_toggle","confidence":"低","description":"部位: 中间区域（躯干/颈部等）\n变化: 两张图视觉上几乎无差异，无明显变化"},
    {"paramId":"Param901","min":0.00,"max":1.00,"suggestedSemantic":"special_middle_purple_ring_rotation","confidence":"低","description":"部位: 特效-中紫环\n变化: 无明显视觉变化"},
    {"paramId":"Param911","min":0.00,"max":1.00,"suggestedSemantic":"special_middle_purple_ring_visibility","confidence":"高","description":"部位: 中间紫环\n变化: 中间的紫色环形特效从隐藏变为显示"},
    {"paramId":"Param961","min":0.00,"max":1.00,"suggestedSemantic":"middle_purple_ring_size","confidence":"低","description":"部位: 中间紫环\n变化: 中间紫环的大小变化，但两张图差异极小，几乎无法察觉"},
    {"paramId":"Param891","min":0.00,"max":1.00,"suggestedSemantic":"special_outer_mask","confidence":"低","description":"部位: 无明显变化部位\n变化: 两张图视觉上几乎无差异，参数变化未导致明显视觉改变"},
    {"paramId":"Param881","min":0.00,"max":1.00,"suggestedSemantic":"inner_purple_ring_rotation","confidence":"低","description":"部位: 内紫环（特效）\n变化: 两张图视觉差异极小，几乎无明显变化"},
    {"paramId":"Param741","min":0.00,"max":1.00,"suggestedSemantic":"inner_purple_ring_visibility","confidence":"低","description":"部位: 内紫环\n变化: 两张图在内紫环的显示状态上无明显视觉差异"},
    {"paramId":"Param731","min":0.00,"max":1.00,"suggestedSemantic":"inner_purple_ring_size","confidence":"低","description":"部位: 内紫环\n变化: 内紫环大小变化不明显"},
    {"paramId":"Param971","min":0.00,"max":1.00,"suggestedSemantic":"special_outer_mask","confidence":"低","description":"部位: 特效-外蒙版\n变化: 无明显视觉变化"},
    {"paramId":"Param981","min":0.00,"max":1.00,"suggestedSemantic":"outer_yellow_ring_rotation","confidence":"低","description":"部位: 外黄环\n变化: 外黄环的旋转角度变化，但两张图视觉差异极小"},
    {"paramId":"Param991","min":0.00,"max":1.00,"suggestedSemantic":"outer_yellow_ring_visibility","confidence":"高","description":"部位: 外黄环（特效）\n变化: 外黄环从隐藏变为显示，在角色头部周围出现黄色环状特效"},
    {"paramId":"Param1001","min":0.00,"max":1.00,"suggestedSemantic":"special_outer_yellow_ring_size","confidence":"低","description":"部位: 外黄环\n变化: 外黄环大小变化，但视觉上差异极小"},
    {"paramId":"Param892","min":0.00,"max":1.00,"suggestedSemantic":"special_inner_mask","confidence":"低","description":"部位: 无明显变化部位\n变化: 两张截图视觉差异极小，几乎无可见变化"},
    {"paramId":"Param1051","min":0.00,"max":1.00,"suggestedSemantic":"special_inner_yellow_ring_rotation","confidence":"低","description":"部位: 特效（黄环相关）\n变化: 两张图视觉差异极小，无明显可辨变化"},
    {"paramId":"Param1031","min":0.00,"max":1.00,"suggestedSemantic":"inner_yellow_ring_visibility","confidence":"低","description":"部位: 内黄环\n变化: 内黄环的显示状态切换，但视觉差异不明显"},
    {"paramId":"Param1021","min":0.00,"max":1.00,"suggestedSemantic":"inner_yellow_ring_size","confidence":"低","description":"部位: 特效-内黄环\n变化: 内黄环大小在最小值和最大值时视觉差异极小，几乎无明显变化"},

    {"paramId":"Param422","min":0.00,"max":1.00,"suggestedSemantic":"special_outer_purple_ring_rotation","confidence":"低","description":"部位: 外紫环\n变化: 无明显视觉变化"},
    {"paramId":"Param902","min":0.00,"max":1.00,"suggestedSemantic":"middle_purple_ring_rotation","confidence":"低","description":"部位: 中紫环\n变化: 无明显视觉变化"},
    {"paramId":"Param882","min":0.00,"max":1.00,"suggestedSemantic":"special_inner_ring_rotation","confidence":"低","description":"部位: 特效（紫环相关）\n变化: 两张图视觉差异极小，几乎无明显变化"},
    {"paramId":"Param982","min":0.00,"max":1.00,"suggestedSemantic":"special_outer_yellow_ring_rotation","confidence":"低","description":"部位: 特效-黄环（外）\n变化: 无明显视觉变化"},
    {"paramId":"Param1052","min":0.00,"max":1.00,"suggestedSemantic":"special_inner_yellow_ring_rotation","confidence":"低","description":"部位: 特效（黄环相关）\n变化: 两张图视觉差异极小，几乎无明显变化"},
    {"paramId":"Param158","min":0.00,"max":1.00,"suggestedSemantic":"无明确对应（因变化极微）","confidence":"低","description":"部位: 无明显变化部位（整体）\n变化: 两张图在Param158最小值和最大值下，角色外观几乎无差异"},
    {"paramId":"Param166","min":0.00,"max":1.00,"suggestedSemantic":"special_purple_ring_visibility","confidence":"高","description":"部位: 特效-紫环\n变化: 背景出现紫色环形特效"},
    {"paramId":"Param167","min":0.00,"max":1.00,"suggestedSemantic":"special_purple_ring_outer_visibility","confidence":"高","description":"部位: 紫环（外）\n变化: 左上角出现紫色环形特效，从无到有"},
    {"paramId":"Param168","min":0.00,"max":1.00,"suggestedSemantic":"special_purple_ring_visibility","confidence":"高","description":"部位: 紫环（特效）\n变化: 左侧出现紫环特效"},
    {"paramId":"Param170","min":0.00,"max":1.00,"suggestedSemantic":"无对应明显变化语义","confidence":"低","description":"部位: 无明显显著变化部位\n变化: 两张图视觉上几乎无差异，参数变化未引起明显视觉改变"},
    {"paramId":"Param171","min":0.00,"max":1.00,"suggestedSemantic":"未知","confidence":"低","description":"部位: 无明显变化部位\n变化: 两张图视觉上几乎无差异，未观察到明显变化"},
    {"paramId":"Param451","min":0.00,"max":1.00,"suggestedSemantic":"star_visibility","confidence":"低","description":"部位: 特效-星\n变化: 星星显示状态切换（两张图视觉差异极小）"},
    {"paramId":"Param541","min":0.00,"max":1.00,"suggestedSemantic":"star_size","confidence":"低","description":"部位: 特效-星\n变化: 无明显视觉变化"},
    {"paramId":"Param1071","min":0.00,"max":1.00,"suggestedSemantic":"star_outer_scale","confidence":"低","description":"部位: 特效（星相关）\n变化: 两张图视觉上几乎无差异，未观察到明显变化"},
    {"paramId":"Param1081","min":0.00,"max":1.00,"suggestedSemantic":"star_visibility","confidence":"高","description":"部位: 特效（星显隐）\n变化: 星星特效从隐藏变为显示"},
    {"paramId":"Param11","min":-1.00,"max":1.00,"suggestedSemantic":"hair_physics_2c","confidence":"低","description":"部位: 头发物理2c对应的头发部分\n变化: 头发摆动幅度或形态有细微差异"},
    {"paramId":"Param12","min":-1.00,"max":1.00,"suggestedSemantic":"hair_physics_2c","confidence":"低","description":"部位: 头发物理2cy相关部位（属于头发物理2组）\n变化: 两张图在头发物理效果上无明显差异，变化极小"},
    {"paramId":"Param14","min":-1.00,"max":1.00,"suggestedSemantic":"hair_physics_2b","confidence":"中","description":"部位: 头发物理2b对应的头发部分（如后发或侧发）\n变化: 头发的动态形态有变化，最小值时头发更贴合，最大值时头发摆动幅度或形态略有调整"},
    {"paramId":"Param16","min":-1.00,"max":1.00,"suggestedSemantic":"hair_physics_2b","confidence":"低","description":"部位: 头发物理2（hair_physics_2b）\n变化: 两张图在头发物理2相关的部位几乎没有明显视觉差异"},
    {"paramId":"Param17","min":-1.00,"max":1.00,"suggestedSemantic":"hair_physics_2a","confidence":"低","description":"部位: 头发物理2a相关部位（后发/鬓发等）\n变化: 无明显变化或变化极细微"},
    {"paramId":"Param18","min":-1.00,"max":1.00,"suggestedSemantic":"hair_physics_2y","confidence":"低","description":"部位: 头发物理2y\n变化: 头发物理效果的摆动幅度或位置有细微差异，但两张图视觉差异极小"},

    {"paramId":"Param93","min":0.00,"max":1.00,"suggestedSemantic":"hand_base_toggle","confidence":"低","description":"部位: 右手\n变化: 右手基础状态切换导致的细微形态差异（两张图视觉上差异极小）"},
    {"paramId":"Param94","min":-30.00,"max":60.00,"suggestedSemantic":"arm_right_upper","confidence":"高","description":"部位: 右上臂\n变化: 右上臂从下垂状态旋转抬起，手臂角度明显变化"},
    {"paramId":"Param95","min":-30.00,"max":30.00,"suggestedSemantic":"hand_layer_95","confidence":"低","description":"部位: 右手（手掌/手指透视）\n变化: 右手区域的透视效果变化不明显，视觉上难以察觉显著差异"},
    {"paramId":"Param117","min":-30.00,"max":30.00,"suggestedSemantic":"hand_layer_98","confidence":"低","description":"部位: 右手（图层透视相关）\n变化: 两张图在右手部位无明显视觉差异，变化极小"},
    {"paramId":"Param97","min":-30.00,"max":30.00,"suggestedSemantic":"arm_right_upper_rotation","confidence":"低","description":"部位: 右上臂\n变化: 右上臂旋转角度变化不明显，两张图几乎无差异"},
    {"paramId":"Param98","min":-30.00,"max":30.00,"suggestedSemantic":"hand_layer_98","confidence":"高","description":"部位: 右手下壁\n变化: 右手下壁的透视效果随参数值变化而调整"},
    {"paramId":"Param116","min":-30.00,"max":30.00,"suggestedSemantic":"hand_layer_98","confidence":"低","description":"部位: 右手（图层透视相关）\n变化: 两张图几乎无差异，变化极小"},
    {"paramId":"Param108","min":0.00,"max":1.00,"suggestedSemantic":"right_hand_layer_order","confidence":"低","description":"部位: 右手（图层顺序相关）\n变化: 两张图在右手部分的视觉差异极小，几乎无明显变化"},
    {"paramId":"Param118","min":0.00,"max":30.00,"suggestedSemantic":"arm_right_extension","confidence":"低","description":"部位: 右手\n变化: 右手伸出程度有细微变化，但整体差异不明显"},
    {"paramId":"Param119","min":0.00,"max":1.00,"suggestedSemantic":"hand_layer_adjustment","confidence":"低","description":"部位: 右手（图层调整相关）\n变化: 两张图对比无明显视觉差异，变化极小"},
    {"paramId":"Param120","min":0.00,"max":1.00,"suggestedSemantic":"hand_layer_98","confidence":"低","description":"部位: 右手（图层透视效果相关）\n变化: 两张图在右手区域的透视效果变化极小，几乎无明显视觉差异"},
    {"paramId":"Param99","min":-30.00,"max":30.00,"suggestedSemantic":"hand_right_wrist_z","confidence":"低","description":"部位: 右手腕\n变化: 右手腕在Z轴方向的前后位置有细微变化"},
    {"paramId":"Param100","min":-30.00,"max":30.00,"suggestedSemantic":"hand_layer_100","confidence":"低","description":"部位: 右手（基础手透视相关）\n变化: 两张图对比，右手部分无明显视觉变化"},
    {"paramId":"Param102","min":-30.00,"max":30.00,"suggestedSemantic":"hand_finger_right","confidence":"低","description":"部位: 右手手指\n变化: 两张图对比，右手手指区域无明显视觉变化"},
    {"paramId":"Param103","min":-30.00,"max":30.00,"suggestedSemantic":"hand_right_finger_2","confidence":"低","description":"部位: 右手手指2\n变化: 右手手指2的形态有细微变化，整体差异不显著"},
    {"paramId":"Param105","min":-30.00,"max":30.00,"suggestedSemantic":"hand_right_finger_3","confidence":"低","description":"部位: 右手手指3\n变化: 两张图中右手手指3的形态变化极小，几乎难以察觉"},
    {"paramId":"Param106","min":-30.00,"max":30.00,"suggestedSemantic":"hand_finger_4","confidence":"低","description":"部位: 右手手指4\n变化: 两张图在右手手指4部位几乎无可见变化"},
    {"paramId":"Param107","min":-30.00,"max":30.00,"suggestedSemantic":"hand_finger_5","confidence":"低","description":"部位: 右手手指5\n变化: 两张图在右手手指5部位无明显视觉差异"},
    {"paramId":"Param92","min":0.00,"max":1.00,"suggestedSemantic":"hand_right_switch","confidence":"高","description":"部位: 右手指\n变化: 右手的手部形态发生切换，从一种状态变为另一种状态"},
    {"paramId":"Param110","min":-30.00,"max":30.00,"suggestedSemantic":"hand_finger_z","confidence":"低","description":"部位: 右手指\n变化: 两张图对比，右手部位无明显视觉变化"},
    {"paramId":"Param111","min":-30.00,"max":30.00,"suggestedSemantic":"hand_finger_right","confidence":"低","description":"部位: 右手指\n变化: 两张图对比，右手手指的形态变化非常细微"},
    {"paramId":"Param112","min":-30.00,"max":30.00,"suggestedSemantic":"hand_finger_2","confidence":"低","description":"部位: 右手手指2\n变化: 两张图在右手手指2部位无明显视觉差异"},
    {"paramId":"Param113","min":-30.00,"max":30.00,"suggestedSemantic":"hand_finger_3","confidence":"低","description":"部位: 右手指（手指3）\n变化: 两张图在右手手指3部位几乎无可见变化"},
    {"paramId":"Param114","min":-30.00,"max":30.00,"suggestedSemantic":"hand_finger_4","confidence":"低","description":"部位: 右手手指4\n变化: 两张图在右手手指4的视觉变化非常细微"},
    {"paramId":"Param115","min":-30.00,"max":30.00,"suggestedSemantic":"finger_right_5","confidence":"低","description":"部位: 右手指5\n变化: 两张图在右手手指5部位无明显视觉差异"},

    {"paramId":"Param91","min":-1.00,"max":1.00,"suggestedSemantic":"hair_ornament_3","confidence":"高","description":"部位: 发饰（饰C）\n变化: 头饰的流苏或装饰部件位置发生明显偏移"},
    {"paramId":"Param96","min":-1.00,"max":1.00,"suggestedSemantic":"ornament_cy","confidence":"低","description":"部位: 饰品（CY）\n变化: 两张图在Param96取最小值和最大值时视觉差异极小"},
    {"paramId":"Param74","min":-1.00,"max":1.00,"suggestedSemantic":"hair_ornament_2","confidence":"高","description":"部位: 发饰（饰品b）\n变化: 头饰上的饰品b位置发生明显变化"},
    {"paramId":"Param88","min":-1.00,"max":1.00,"suggestedSemantic":"hair_ornament_by","confidence":"中","description":"部位: 发饰by\n变化: 发饰by的装饰长度发生变化，第一张较长，第二张较短"},
    {"paramId":"Param89","min":-1.00,"max":1.00,"suggestedSemantic":"hair_ornament_a","confidence":"低","description":"部位: 发饰a\n变化: 两张图在发饰a部位的变化极小，几乎无明显视觉差异"},
    {"paramId":"Param90","min":-1.00,"max":1.00,"suggestedSemantic":"","confidence":"-","description":"API 错误: Request timeout"},

    {"paramId":"Param43","min":-1.00,"max":1.00,"suggestedSemantic":"hair_back_4","confidence":"高","description":"部位: 后发d\n变化: 后发部分的形态发生明显变化，从较为紧凑的状态变为更舒展的状态"},
    {"paramId":"Param44","min":-1.00,"max":1.00,"suggestedSemantic":"hair_back_4_y","confidence":"低","description":"部位: 后发d\n变化: 后发d的y轴位置发生细微调整（变化不显著）"},
    {"paramId":"Param45","min":-1.00,"max":1.00,"suggestedSemantic":"hair_back_3","confidence":"高","description":"部位: 后发c\n变化: 后发c的形态（如长度、角度）发生明显变化"},
    {"paramId":"Param54","min":-1.00,"max":1.00,"suggestedSemantic":"hair_back_c","confidence":"高","description":"部位: 后发\n变化: 后发（尤其是两侧粉色长发部分）从紧凑状态变为展开扩展状态"},
    {"paramId":"Param55","min":-1.00,"max":1.00,"suggestedSemantic":"hair_back_2","confidence":"高","description":"部位: 后发b\n变化: 后发位置发生明显移动，形态也有相应变化"},
    {"paramId":"Param56","min":-1.00,"max":1.00,"suggestedSemantic":"hair_back_b","confidence":"高","description":"部位: 后发b\n变化: 后发部分从较短状态变为明显延长并扩展的形态"},
    {"paramId":"Param62","min":-1.00,"max":1.00,"suggestedSemantic":"hair_back_1","confidence":"低","description":"部位: 后发a\n变化: 后发a部位的形态变化不明显"},
    {"paramId":"Param73","min":-1.00,"max":1.00,"suggestedSemantic":"hair_back_a","confidence":"高","description":"部位: 后发ay\n变化: 后发ay对应的头发部分形状发生变化"},
    {"paramId":"Param23","min":-1.00,"max":1.00,"suggestedSemantic":"hair_side_3","confidence":"低","description":"部位: 鬓发c\n变化: 两张图中鬓发c的视觉差异极小"},
    {"paramId":"Param30","min":-1.00,"max":1.00,"suggestedSemantic":"hair_side_3","confidence":"低","description":"部位: 鬓发\n变化: 两张图在鬓发区域无明显视觉差异"},
    {"paramId":"Param35","min":-1.00,"max":1.00,"suggestedSemantic":"hair_side_2","confidence":"低","description":"部位: 鬓发b\n变化: 两张图在鬓发b部位无明显视觉变化"},
    {"paramId":"Param40","min":-1.00,"max":1.00,"suggestedSemantic":"hair_side_b","confidence":"低","description":"部位: 鬓发b\n变化: 无明显可见变化"},
    {"paramId":"Param41","min":-1.00,"max":1.00,"suggestedSemantic":"hair_side_1","confidence":"低","description":"部位: 鬓发a\n变化: 两张图在鬓发区域几乎无可见变化"},
    {"paramId":"Param42","min":-1.00,"max":1.00,"suggestedSemantic":"hair_side_1","confidence":"低","description":"部位: 鬓发\n变化: 两张图在鬓发部位几乎无可见变化"},
    {"paramId":"Param19","min":-1.00,"max":1.00,"suggestedSemantic":"hair_back_1B","confidence":"低","description":"部位: 后发B\n变化: 两张图在后发B区域几乎无可见变化"},
    {"paramId":"Param20","min":-1.00,"max":1.00,"suggestedSemantic":"hair_back_1B","confidence":"低","description":"部位: 后发B\n变化: 后发B的形状或位置有细微变化"},
    {"paramId":"Param21","min":-1.00,"max":1.00,"suggestedSemantic":"hair_back_A","confidence":"低","description":"部位: 后发A\n变化: 后发A区域的视觉变化不显著"},
    {"paramId":"Param22","min":-1.00,"max":1.00,"suggestedSemantic":"hair_back_1","confidence":"低","description":"部位: 后发A\n变化: 后发A部分的形态（如长度或角度）有细微变化"},

    {"paramId":"Param5","min":-1.00,"max":1.00,"suggestedSemantic":"hair_physics_3","confidence":"低","description":"部位: 刘海c\n变化: 刘海的物理效果变化不明显"},
    {"paramId":"Param6","min":-1.00,"max":1.00,"suggestedSemantic":"hair_physics_1","confidence":"低","description":"部位: 刘海\n变化: 刘海的物理模拟效果变化，最小值时刘海更贴合，最大值时刘海摆动幅度增大"},
    {"paramId":"Param7","min":-1.00,"max":1.00,"suggestedSemantic":"hair_physics_b","confidence":"低","description":"部位: 刘海b\n变化: 刘海的物理模拟效果变化（如摆动幅度）"},
    {"paramId":"Param8","min":-1.00,"max":1.00,"suggestedSemantic":"hair_physics_by","confidence":"低","description":"部位: 刘海物理效果\n变化: 刘海的物理摆动或形态变化极小"},
    {"paramId":"Param9","min":-1.00,"max":1.00,"suggestedSemantic":"hair_physics_a","confidence":"低","description":"部位: 刘海a\n变化: 两张图中刘海的物理效果变化极小"},
    {"paramId":"Param10","min":-1.00,"max":1.00,"suggestedSemantic":"hair_physics_ay","confidence":"低","description":"部位: 刘海\n变化: 无明显视觉变化"},

    {"paramId":"Param101","min":0.00,"max":1.00,"suggestedSemantic":"special_dark_face","confidence":"低","description":"部位: 面部\n变化: 两张图的面部视觉效果几乎无差异"},
    {"paramId":"Param104","min":0.00,"max":1.00,"suggestedSemantic":"special_angry","confidence":"低","description":"部位: 面部（眉毛/眼睛/嘴巴）\n变化: 无明显可见变化"},
    {"paramId":"Param109","min":0.00,"max":1.00,"suggestedSemantic":"无明确对应","confidence":"低","description":"部位: 无明显变化部位\n变化: 两张图视觉差异极小"},
    {"paramId":"Param122","min":0.00,"max":1.00,"suggestedSemantic":"special_money","confidence":"低","description":"部位: 特效（钱）\n变化: 无明显视觉变化"},
    {"paramId":"Param130","min":0.00,"max":1.00,"suggestedSemantic":"special_tear","confidence":"低","description":"部位: 无显著变化部位\n变化: 两张图在视觉上无明显差异"},

    {"paramId":"Param66","min":-30.00,"max":30.00,"suggestedSemantic":"head_angle_x/head_angle_y/head_angle_z","confidence":"低","description":"部位: 头部（头XYZ组相关）\n变化: 两张图视觉差异极小"},
    {"paramId":"Param169","min":-30.00,"max":30.00,"suggestedSemantic":"head_x","confidence":"低","description":"部位: 头部\n变化: 两张图视觉差异极小"},
    {"paramId":"Param248","min":-30.00,"max":30.00,"suggestedSemantic":"head_y","confidence":"低","description":"部位: 头部（Y轴位置）\n变化: 无明显视觉变化"},
    {"paramId":"Param25","min":-30.00,"max":30.00,"suggestedSemantic":"head_angle_x","confidence":"低","description":"部位: 头部\n变化: 两张图对比无明显视觉变化"},
    {"paramId":"Param249","min":-30.00,"max":30.00,"suggestedSemantic":"head_angle_z","confidence":"低","description":"部位: 头部（头XYZ组相关）\n变化: 无明显视觉变化"},

    {"paramId":"ParamBodyAngleX2","min":-10.00,"max":10.00,"suggestedSemantic":"body_angle_x","confidence":"低","description":"部位: 身体（躯干）\n变化: 身体（躯干）的X轴旋转角度变化，整体身体左右倾斜程度存在细微差异"},
    {"paramId":"ParamBodyAngleY2","min":-10.00,"max":10.00,"suggestedSemantic":"body_angle_y2","confidence":"低","description":"部位: 躯干\n变化: 身体（躯干）在Y轴方向的旋转角度变化，但变化不显著"},
    {"paramId":"ParamBodyAngleZ2","min":-10.00,"max":10.00,"suggestedSemantic":"body_angle_z2","confidence":"高","description":"部位: 躯干\n变化: 角色身体（躯干）沿Z轴旋转，从第一张的倾斜方向变为第二张的相反倾斜方向"},
    {"paramId":"Param164","min":-10.00,"max":10.00,"suggestedSemantic":"leg_l_lift/leg_l_swing/leg_r_swing","confidence":"低","description":"部位: 腿\n变化: 无明显视觉变化"},
    {"paramId":"Param125","min":-10.00,"max":10.00,"suggestedSemantic":"body_angle_x","confidence":"中","description":"部位: 躯干\n变化: 躯干的X方向角度发生变化，导致上半身左右倾斜"},
    {"paramId":"Param123","min":-10.00,"max":10.00,"suggestedSemantic":"body_y_animation","confidence":"低","description":"部位: 身体整体（躯干Y方向）\n变化: 两张图几乎无视觉差异"},
    {"paramId":"Param124","min":-10.00,"max":10.00,"suggestedSemantic":"body_z","confidence":"高","description":"部位: 躯干\n变化: 躯干在Z轴方向的前后位置发生变化"},
    {"paramId":"Param128","min":-1.00,"max":1.00,"suggestedSemantic":"body_angle_x","confidence":"高","description":"部位: 躯干\n变化: 躯干的角度发生旋转变化"},
    {"paramId":"Param153","min":-10.00,"max":10.00,"suggestedSemantic":"shoulder_lift","confidence":"低","description":"部位: 肩\n变化: 肩部上下移动幅度变化（视觉上变化极细微）"},
    {"paramId":"Param251","min":-10.00,"max":10.00,"suggestedSemantic":"body_translation_x","confidence":"高","description":"部位: 躯干\n变化: 身体水平位置向右移动，整体角色在画面中右移"},
    {"paramId":"Param165","min":-10.00,"max":10.00,"suggestedSemantic":"leg_l_lift","confidence":"低","description":"部位: 腿/裙摆\n变化: 两张图在腿部和裙摆区域无明显视觉差异"},
    {"paramId":"Param126","min":-10.00,"max":10.00,"suggestedSemantic":"leg_swing","confidence":"低","description":"部位: 腿（左右腿摆动）\n变化: 腿的摆动幅度有细微变化"},
    {"paramId":"Param127","min":-10.00,"max":10.00,"suggestedSemantic":"leg_l_swing或leg_r_swing","confidence":"低","description":"部位: 腿/裙摆\n变化: 两张图在腿和裙摆区域无明显视觉差异"},
    {"paramId":"Param129","min":-10.00,"max":10.00,"suggestedSemantic":"leg_r_swing","confidence":"低","description":"部位: 右腿\n变化: 右腿X方向位移变化，视觉上差异极小"},
    {"paramId":"Param131","min":-10.00,"max":10.00,"suggestedSemantic":"leg_r_animation_perspective","confidence":"低","description":"部位: 腿（动画R透视相关）\n变化: 无明显视觉变化"},

    {"paramId":"Param121","min":0.00,"max":1.00,"suggestedSemantic":"special_outer_mask","confidence":"高","description":"部位: 镜头（黑幕效果）\n变化: 画面从显示角色变为全黑，角色完全不可见"},
    {"paramId":"Param137","min":0.00,"max":1.00,"suggestedSemantic":"special_outer_mask","confidence":"低","description":"部位: 特效（黑幕透明显现相关）\n变化: 两张图视觉上无明显差异"},
    {"paramId":"Param138","min":-1.00,"max":1.00,"suggestedSemantic":"camera_x或camera_y或character_scale","confidence":"低","description":"部位: 镜头相关透明效果\n变化: 两张图视觉差异极小"},
    {"paramId":"Param139","min":-1.00,"max":1.00,"suggestedSemantic":"camera_y","confidence":"低","description":"部位: 无显著变化部位\n变化: 两张截图在视觉上几乎无差异"},
    {"paramId":"Param132","min":0.00,"max":1.00,"suggestedSemantic":"special_eye_glow","confidence":"高","description":"部位: 眼睛（发光效果）\n变化: 眼睛的紫色发光效果在Param132取最大值时显著增强"},
    {"paramId":"Param136","min":0.00,"max":1.00,"suggestedSemantic":"camera_effect_opacity","confidence":"低","description":"部位: 整体（镜头相关特效）\n变化: 两张图视觉差异极小"},
    {"paramId":"Param133","min":0.00,"max":1.00,"suggestedSemantic":"camera_x/camera_y/character_scale","confidence":"低","description":"部位: 镜头相关\n变化: 两张图视觉上几乎无差异"},
    {"paramId":"Param134","min":-1.00,"max":1.00,"suggestedSemantic":"camera_x","confidence":"低","description":"部位: 镜头X（camera_x）\n变化: 镜头X方向位移导致角色在画面中左右移动，但视觉变化不明显"},
    {"paramId":"Param135","min":-1.00,"max":1.00,"suggestedSemantic":"camera_y","confidence":"低","description":"部位: 镜头（camera_y）\n变化: 无明显视觉变化"},
    {"paramId":"Param154","min":0.00,"max":1.00,"suggestedSemantic":"seven_star_disc_transparency","confidence":"低","description":"部位: 七星盘（胸口紫色宝石）\n变化: 七星盘的透明度变化，但视觉上差异极小"},
    {"paramId":"Param155","min":-30.00,"max":30.00,"suggestedSemantic":"camera_x","confidence":"低","description":"部位: 镜头X（camera_x）\n变化: 两张截图均为全黑"},
    {"paramId":"Param156","min":-30.00,"max":30.00,"suggestedSemantic":"camera_y","confidence":"低","description":"部位: 镜头（整体视角）\n变化: 两张图中角色在画面中的上下位置无明显差异"},
    {"paramId":"Param157","min":-30.00,"max":30.00,"suggestedSemantic":"character_scale","confidence":"高","description":"部位: 整体角色（镜头控制）\n变化: 角色整体尺寸显著增大"},

    {"paramId":"Param","min":-1.00,"max":1.00,"suggestedSemantic":"eye_ball_rx","confidence":"低","description":"部位: 眼球（左右眼）\n变化: 眼球左右旋转角度变化，但视觉差异极小"},
    {"paramId":"Param2","min":-1.00,"max":1.00,"suggestedSemantic":"eye_ball_y","confidence":"低","description":"部位: 眼球（左右眼）\n变化: 无明显可见变化或变化极细微"},
    {"paramId":"Param63","min":-1.00,"max":1.00,"suggestedSemantic":"eye_highlight_r","confidence":"低","description":"部位: 右眼高光\n变化: 右眼高光区域的亮度或大小有细微变化"},
    {"paramId":"Param64","min":-1.00,"max":1.00,"suggestedSemantic":"eye_r_highlight","confidence":"低","description":"部位: 右眼高光\n变化: 右眼高光强度变化，但两张图视觉差异极小"},
    {"paramId":"Param65","min":-1.00,"max":1.00,"suggestedSemantic":"eye_light_r","confidence":"低","description":"部位: 右眼\n变化: 无明显视觉变化"},
    {"paramId":"Param69","min":-1.00,"max":1.00,"suggestedSemantic":"eye_light_r2","confidence":"低","description":"部位: 右眼（或左眼）光点效果\n变化: 两张图在眼部光点效果上无明显视觉差异"},
    {"paramId":"Param159","min":-1.00,"max":1.00,"suggestedSemantic":"eye_orbit_r_physics","confidence":"低","description":"部位: 右眼眶\n变化: 两张图在右眼眶区域几乎无明显视觉变化"},
    {"paramId":"Param160","min":-1.00,"max":1.00,"suggestedSemantic":"eye_orbit_r_physics","confidence":"低","description":"部位: 右眼眶\n变化: 两张图视觉上无明显差异"},
    {"paramId":"Param13","min":-1.00,"max":1.00,"suggestedSemantic":"","confidence":"-","description":"API 错误: API 调用参数有误，请检查文档。"},
    {"paramId":"Param3","min":-1.00,"max":1.00,"suggestedSemantic":"eye_ball_x","confidence":"低","description":"部位: 左眼/右眼（眼球物理LX）\n变化: 两张图在眼球位置或物理效果上几乎无差异"},
    {"paramId":"Param4","min":-1.00,"max":1.00,"suggestedSemantic":"eye_ball_y","confidence":"低","description":"部位: 眼球（或左眼/右眼）\n变化: 眼球的上下位置有轻微变化"},
    {"paramId":"Param161","min":-1.00,"max":1.00,"suggestedSemantic":"eye_orbit_physics","confidence":"低","description":"部位: 眼眶（眼部区域）\n变化: 两张图在眼部区域无明显视觉差异"},
    {"paramId":"Param162","min":-1.00,"max":1.00,"suggestedSemantic":"eye_orbit_physics_l2","confidence":"低","description":"部位: 眼眶（眼部区域）\n变化: 两张图在眼部区域的视觉差异极小"},
    {"paramId":"Param67","min":-1.00,"max":1.00,"suggestedSemantic":"eye_highlight_l","confidence":"低","description":"部位: 眼睛（左眼/右眼）\n变化: 无明显视觉变化"},
    {"paramId":"Param68","min":-1.00,"max":1.00,"suggestedSemantic":"eye_highlight_r","confidence":"低","description":"部位: 右眼高光\n变化: 眼睛高光在最小值和最大值时无明显视觉差异"},
    {"paramId":"Param70","min":-1.00,"max":1.00,"suggestedSemantic":"eye_light_effect","confidence":"低","description":"部位: 眼睛（光点效果）\n变化: 两张图在眼睛区域的光点效果差异极小"},
    {"paramId":"Param71","min":-1.00,"max":1.00,"suggestedSemantic":"eye_light_effect","confidence":"低","description":"部位: 眼部（光点L2相关）\n变化: 两张图在眼部区域无明显视觉差异"},
    {"paramId":"Param15","min":-1.00,"max":1.00,"suggestedSemantic":"eye_l_lash","confidence":"低","description":"部位: 左眼睫毛\n变化: 左眼睫毛的形态或长度有细微变化"},
    {"paramId":"Param149","min":-1.00,"max":1.00,"suggestedSemantic":"eye_r_open","confidence":"高","description":"部位: 右眼\n变化: 右眼从眯眼状态变为睁眼状态"},
    {"paramId":"Param150","min":-1.00,"max":1.00,"suggestedSemantic":"eye_open","confidence":"高","description":"部位: 眼睛（整体）\n变化: 眼睛从眯眼状态变为瞪眼状态"},

    {"paramId":"Param250","min":-1.00,"max":1.00,"suggestedSemantic":"mouth_form","confidence":"低","description":"部位: 嘴巴\n变化: 两张图中嘴巴形状无明显差异"},
    {"paramId":"Param163","min":0.00,"max":1.00,"suggestedSemantic":"mouth_opn_y","confidence":"低","description":"部位: 下颌\n变化: 两张图中下颌的开闭程度变化极小"},
    {"paramId":"ParamBrowRForm","min":-1.00,"max":1.00,"suggestedSemantic":"brow_r_form","confidence":"高","description":"部位: 右眉\n变化: 右眉形态发生变形，从较平缓的形状变为更弯曲或调整后的形态"},
    {"paramId":"ParamBrowRAngle","min":-1.00,"max":1.00,"suggestedSemantic":"brow_r_angle","confidence":"高","description":"部位: 右眉\n变化: 右眉角度发生明显变化"},
    {"paramId":"ParamBrowLForm","min":-1.00,"max":1.00,"suggestedSemantic":"brow_l_form","confidence":"高","description":"部位: 左眉\n变化: 左眉的形态发生变形，形状有所改变"},
    {"paramId":"ParamBrowLAngle","min":-1.00,"max":1.00,"suggestedSemantic":"brow_l_angle","confidence":"高","description":"部位: 左眉\n变化: 左眉的倾斜角度发生变化"},

    {"paramId":"Param47","min":-1.00,"max":1.00,"suggestedSemantic":"neck_ornament_3x","confidence":"低","description":"部位: 颈饰\n变化: 无明显视觉变化"},
    {"paramId":"Param46","min":-1.00,"max":1.00,"suggestedSemantic":"neck_ornament_3y","confidence":"低","description":"部位: 颈饰3y\n变化: 两张图在颈饰3y对应部位几乎无可见变化"},
    {"paramId":"Param140","min":-1.00,"max":1.00,"suggestedSemantic":"neck_ornament_2x","confidence":"低","description":"部位: 颈饰2x\n变化: 两张图在颈饰2x参数取最小值和最大值时，颈饰部位几乎无明显视觉差异"},
    {"paramId":"Param141","min":-1.00,"max":1.00,"suggestedSemantic":"neck_ornament_2y","confidence":"低","description":"部位: 颈饰2y（颈部装饰）\n变化: 两张图在颈部颈饰区域无明显视觉差异"},
    {"paramId":"Param142","min":-1.00,"max":1.00,"suggestedSemantic":"neck_ornament_x","confidence":"低","description":"部位: 颈饰\n变化: 无明显视觉变化"},
    {"paramId":"Param143","min":-1.00,"max":1.00,"suggestedSemantic":"neck_ornament_1y","confidence":"低","description":"部位: 颈饰\n变化: 两张图在颈饰区域无明显视觉差异"},

    {"paramId":"Param52","min":-1.00,"max":1.00,"suggestedSemantic":"arm_left_sleeve","confidence":"低","description":"部位: 衣服左饰（左袖区域）\n变化: 两张图在衣服左侧装饰（左袖）区域无明显视觉差异"},
    {"paramId":"Param144","min":-1.00,"max":1.00,"suggestedSemantic":"ornament_left_ring","confidence":"低","description":"部位: 左环\n变化: 两张图中左环的视觉形态无明显差异"},
    {"paramId":"Param145","min":-1.00,"max":1.00,"suggestedSemantic":"ring_left","confidence":"低","description":"部位: 左环\n变化: 两张图中左环的视觉差异极小"},
    {"paramId":"Param148","min":-1.00,"max":1.00,"suggestedSemantic":"ring_right_3","confidence":"低","description":"部位: 右环\n变化: 无明显视觉变化"},
    {"paramId":"Param146","min":-1.00,"max":1.00,"suggestedSemantic":"right_ring_2","confidence":"低","description":"部位: 右环（饰品）\n变化: 两张图视觉差异极小"},
    {"paramId":"Param151","min":-1.00,"max":1.00,"suggestedSemantic":"right_ring_1","confidence":"低","description":"部位: 右环\n变化: 两张图中右环的视觉差异极小"},

    {"paramId":"Param58","min":-1.00,"max":1.00,"suggestedSemantic":"belt_left_3","confidence":"低","description":"部位: 左衣带子（或左袖相关）\n变化: 两张图对比无明显视觉变化"},
    {"paramId":"Param147","min":-1.00,"max":1.00,"suggestedSemantic":"belt_left_2","confidence":"低","description":"部位: 左衣带\n变化: 无明显视觉变化"},
    {"paramId":"Param152","min":-1.00,"max":1.00,"suggestedSemantic":"belt_left_1","confidence":"低","description":"部位: 左衣带\n变化: 两张图在左衣带部位几乎无可见变化"},
    {"paramId":"Param61","min":-1.00,"max":1.00,"suggestedSemantic":"right_belt_3","confidence":"低","description":"部位: 右衣带子3\n变化: 两张图在右衣带子3部位几乎无可见变化"},
    {"paramId":"Param72","min":-1.00,"max":1.00,"suggestedSemantic":"belt_right_2","confidence":"低","description":"部位: 右衣带\n变化: 无明显视觉变化"},
    {"paramId":"Param48","min":-1.00,"max":1.00,"suggestedSemantic":"right_belt_2","confidence":"低","description":"部位: 右衣带\n变化: 两张图在右衣带部位几乎无可见变化"},

    {"paramId":"Param49","min":-1.00,"max":1.00,"suggestedSemantic":"ear_disc_x4","confidence":"低","description":"部位: 兽耳相关部位\n变化: 两张图视觉差异极小"},
    {"paramId":"Param50","min":-1.00,"max":1.00,"suggestedSemantic":"无明确对应语义名","confidence":"低","description":"部位: 无明显变化部位\n变化: 两张图视觉差异极小"},
    {"paramId":"Param51","min":-1.00,"max":1.00,"suggestedSemantic":"无明确对应（因变化不显著）","confidence":"低","description":"部位: 无明显变化部位\n变化: 两张截图几乎无视觉差异"},
    {"paramId":"Param53","min":-1.00,"max":1.00,"suggestedSemantic":"ear_y3","confidence":"低","description":"部位: 头部（兽耳相关部位）\n变化: 两张图视觉差异极小"},
    {"paramId":"Param57","min":-1.00,"max":1.00,"suggestedSemantic":"ear_disc_x2","confidence":"低","description":"部位: 兽耳（圆盘X2相关）\n变化: 两张图视觉差异极小"},
    {"paramId":"Param59","min":-1.00,"max":1.00,"suggestedSemantic":"ear_y2","confidence":"低","description":"部位: 兽耳\n变化: 无明显视觉变化"},
    {"paramId":"Param60","min":-1.00,"max":1.00,"suggestedSemantic":"无明确对应（或变化过小无法判定）","confidence":"低","description":"部位: 无明显变化部位\n变化: 两张图视觉差异极小"},
    {"paramId":"Param75","min":-1.00,"max":1.00,"suggestedSemantic":"neck_ornament_y","confidence":"低","description":"部位: 颈饰（紫色圆盘）\n变化: 颈部的紫色圆盘在y轴方向有极轻微的位置变化"},

    {"paramId":"Param76","min":-1.00,"max":1.00,"suggestedSemantic":"skirt_curtain_3x","confidence":"低","description":"部位: 帘子（裙子部分）\n变化: 帘子3x参数变化导致对应帘子部件的形态或位置有细微调整"},
    {"paramId":"Param77","min":-1.00,"max":1.00,"suggestedSemantic":"skirt_curtain_3y","confidence":"低","description":"部位: 裙子帘子\n变化: 无明显可见变化或变化极细微"},
    {"paramId":"Param80","min":-1.00,"max":1.00,"suggestedSemantic":"skirt_curtain_2x","confidence":"低","description":"部位: 帘子（裙子部分）\n变化: 两张图中帘子部分无明显可见变化"},
    {"paramId":"Param81","min":-1.00,"max":1.00,"suggestedSemantic":"skirt_curtain_2y","confidence":"低","description":"部位: 帘子（裙子部分）\n变化: 无明显视觉变化"},
    {"paramId":"Param78","min":-1.00,"max":1.00,"suggestedSemantic":"skirt_curtain_1x","confidence":"低","description":"部位: 帘子\n变化: 无明显视觉变化或变化极细微"},
    {"paramId":"Param79","min":-1.00,"max":1.00,"suggestedSemantic":"skirt_curtain_1y","confidence":"低","description":"部位: 帘子（裙子部分）\n变化: 两张图在帘子（裙子相关装饰）区域无明显视觉差异"},

    {"paramId":"Param82","min":-1.00,"max":1.00,"suggestedSemantic":"skirt_drive_3","confidence":"高","description":"部位: 裙摆3x\n变化: 裙摆3x部分的展开程度发生变化"},
    {"paramId":"Param83","min":-1.00,"max":1.00,"suggestedSemantic":"skirt_drive_3y","confidence":"低","description":"部位: 裙摆3y\n变化: 两张图中裙摆3y的形态和位置几乎无差异"},
    {"paramId":"Param87","min":-1.00,"max":1.00,"suggestedSemantic":"skirt_drive_2","confidence":"中","description":"部位: 裙摆\n变化: 裙摆的形态和位置发生明显变化"},
    {"paramId":"Param86","min":-1.00,"max":1.00,"suggestedSemantic":"skirt_drive_y2","confidence":"高","description":"部位: 裙子\n变化: 裙摆的垂直位置发生明显变化"},
    {"paramId":"Param84","min":-1.00,"max":1.00,"suggestedSemantic":"skirt_drive_1","confidence":"高","description":"部位: 裙摆(1x)\n变化: 裙摆1x部分的展开程度发生变化"},
    {"paramId":"Param85","min":-1.00,"max":1.00,"suggestedSemantic":"skirt_drive_1y","confidence":"高","description":"部位: 裙摆1y\n变化: 裙摆1y部分的展开程度或形状发生变化"},
    {"paramId":"Param24","min":-1.00,"max":1.00,"suggestedSemantic":"skirt_tassel_1","confidence":"低","description":"部位: 裙子穗子\n变化: 两张图视觉差异极小"},
    {"paramId":"Param26","min":-1.00,"max":1.00,"suggestedSemantic":"skirt_tassel_2","confidence":"低","description":"部位: 穗子（裙子部分）\n变化: 两张图中穗子的形态、位置或显隐状态几乎无差异"},
    {"paramId":"Param27","min":-1.00,"max":1.00,"suggestedSemantic":"skirt_tassel_1l","confidence":"低","description":"部位: 穗子（裙摆）\n变化: 穗子位置/形态无明显变化"},
    {"paramId":"Param28","min":-1.00,"max":1.00,"suggestedSemantic":"skirt_tassel_2l","confidence":"低","description":"部位: 裙子穗子\n变化: 两张图在穗子部位的变化非常细微"},
    {"paramId":"Param29","min":-1.00,"max":1.00,"suggestedSemantic":"","confidence":"-","description":"API 错误: Request timeout"},

    {"paramId":"Param31","min":-1.00,"max":1.00,"suggestedSemantic":"arm_right_rotation","confidence":"低","description":"部位: 右手臂\n变化: 右手臂的旋转角度或位置有细微变化"},
    {"paramId":"Param32","min":-1.00,"max":1.00,"suggestedSemantic":"arm_right_mid","confidence":"低","description":"部位: 右手臂\n变化: 右手臂的形态或位置有细微变化"},
    {"paramId":"Param33","min":-1.00,"max":1.00,"suggestedSemantic":"arm_right_upper","confidence":"低","description":"部位: 右手臂（右上臂/右前臂）\n变化: 右手臂角度或位置有细微变化"},
    {"paramId":"Param34","min":-1.00,"max":1.00,"suggestedSemantic":"arm_left_mid","confidence":"低","description":"部位: 左臂（左袖）\n变化: 左臂袖子的角度或位置有细微变化"},
    {"paramId":"Param36","min":-1.00,"max":1.00,"suggestedSemantic":"arm_left_mid","confidence":"低","description":"部位: 左臂（左袖）\n变化: 左臂袖子的形态或位置变化不明显"},
    {"paramId":"Param37","min":-1.00,"max":1.00,"suggestedSemantic":"arm_left_upper","confidence":"低","description":"部位: 左臂（手臂L3）\n变化: 两张图在左臂相关部位无明显视觉差异"},
    {"paramId":"Param38","min":-1.00,"max":1.00,"suggestedSemantic":"arm_left_sleeve","confidence":"低","description":"部位: 左袖\n变化: 两张图中左袖的视觉差异极小"},
    {"paramId":"Param39","min":-1.00,"max":1.00,"suggestedSemantic":"arm_left_sleeve","confidence":"低","description":"部位: 左袖\n变化: 无明显视觉变化或变化极细微"},

    {"paramId":"Param_Angle_Rotation_1_ArtMesh272","min":-45.00,"max":45.00,"suggestedSemantic":"hair_ornament_1","confidence":"高","description":"部位: 左丝带\n变化: 第一张图左侧有剑，第二张图左侧无剑，左丝带旋转参数变化导致剑的显隐变化"},
    {"paramId":"Param_Angle_Rotation_2_ArtMesh272","min":-45.00,"max":45.00,"suggestedSemantic":"hair_ornament_1","confidence":"低","description":"部位: 左丝带\n变化: 左丝带的角度发生旋转变化，从-45.00到45.00时丝带位置有细微调整"},
    {"paramId":"Param_Angle_Rotation_3_ArtMesh272","min":-45.00,"max":45.00,"suggestedSemantic":"arm_left_silk_ribbon_rotation","confidence":"高","description":"部位: 左丝带\n变化: 左丝带的旋转角度发生变化，导致丝带的倾斜方向和形态出现明显改变"},
    {"paramId":"Param_Angle_Rotation_4_ArtMesh272","min":-45.00,"max":45.00,"suggestedSemantic":"hair_ornament_3","confidence":"低","description":"部位: 左丝带\n变化: 左丝带旋转角度变化，但视觉上变化不明显"},
    {"paramId":"Param_Angle_Rotation_5_ArtMesh272","min":-45.00,"max":45.00,"suggestedSemantic":"left_ribbon_rotation","confidence":"低","description":"部位: 左丝带\n变化: 左丝带旋转角度变化，但视觉上差异极小"},
    {"paramId":"Param_Angle_Rotation_6_ArtMesh272","min":-45.00,"max":45.00,"suggestedSemantic":"ribbon_left_rotation","confidence":"高","description":"部位: 左丝带\n变化: 左丝带旋转角度变化，导致丝带形态改变"},
    {"paramId":"Param_Angle_Rotation_7_ArtMesh272","min":-45.00,"max":45.00,"suggestedSemantic":"hair_ornament_1","confidence":"高","description":"部位: 左丝带\n变化: 左丝带旋转角度从-45度变为45度，导致丝带呈现相反方向的倾斜"},
    {"paramId":"Param_Angle_Rotation_1_ArtMesh249","min":-45.00,"max":45.00,"suggestedSemantic":"hair_ornament_2","confidence":"高","description":"部位: 右丝带\n变化: 右丝带旋转角度变化，导致右臂的剑（或相关武器部件）出现"},
    {"paramId":"Param_Angle_Rotation_2_ArtMesh249","min":-45.00,"max":45.00,"suggestedSemantic":"hair_ornament_2","confidence":"高","description":"部位: 右丝带\n变化: 右丝带旋转后形态发生明显变化"},
    {"paramId":"Param_Angle_Rotation_3_ArtMesh249","min":-45.00,"max":45.00,"suggestedSemantic":"arm_right_sash_rotation","confidence":"高","description":"部位: 右丝带\n变化: 右丝带旋转角度变化导致右臂姿态改变（剑显现）"},
    {"paramId":"Param_Angle_Rotation_4_ArtMesh249","min":-45.00,"max":45.00,"suggestedSemantic":"ribbon_right","confidence":"高","description":"部位: 右丝带\n变化: 右丝带旋转角度从-45.00变为45.00，右丝带角度明显旋转"},
    {"paramId":"Param_Angle_Rotation_5_ArtMesh249","min":-45.00,"max":45.00,"suggestedSemantic":"ribbon_right_rotation","confidence":"高","description":"部位: 右丝带\n变化: 右丝带的角度发生明显变化"},
    {"paramId":"Param_Angle_Rotation_6_ArtMesh249","min":-45.00,"max":45.00,"suggestedSemantic":"hair_ornament_2","confidence":"高","description":"部位: 右丝带\n变化: 右丝带旋转角度变化，导致其形态发生明显改变"},
    {"paramId":"Param_Angle_Rotation_7_ArtMesh249","min":-45.00,"max":45.00,"suggestedSemantic":"right_ribbon_angle","confidence":"低","description":"部位: 右丝带\n变化: 右丝带旋转角度变化，导致丝带形态/位置有细微调整"}
]

out_dir = 'D:/Unity/projects/Desktop_per_pro/code/desktop_unity/Assets/Resources/Live2D/ParamMaps'
os.makedirs(out_dir, exist_ok=True)

now = datetime.datetime.now().strftime('%Y-%m-%dT%H:%M:%S')

calibrations = []
for r in vision_scan_results:
    desc = r['description']
    lines = desc.split('\n')
    bodyPart = ''
    visualChange = ''
    for line in lines:
        if line.startswith('部位: '):
            bodyPart = line[4:].strip()
        elif line.startswith('变化: '):
            visualChange = line[4:].strip()
    visualDesc = desc.replace('\n', '；')

    entry = {
        'paramId': r['paramId'],
        'semantic': r.get('suggestedSemantic', ''),
        'min': r['min'],
        'max': r['max'],
        'defaultValue': 0.0,
        'bodyPart': bodyPart,
        'visualChange': visualChange,
        'visualDescription': visualDesc,
        'confidence': r.get('confidence', ''),
        'calibratedAt': now
    }
    calibrations.append(entry)

out = {
    'formatVersion': '1.0',
    'modelName': '符玄',
    'generatedAt': '2026-07-07T04:22:08',
    'calibrations': calibrations
}

out_path = os.path.join(out_dir, 'vision_calibration.json')
with open(out_path, 'w', encoding='utf-8') as f:
    json.dump(out, f, ensure_ascii=False, indent=2)

success = sum(1 for c in calibrations if c['confidence'] == '高')
medium = sum(1 for c in calibrations if c['confidence'] == '中')
low = sum(1 for c in calibrations if c['confidence'] == '低')
errors = sum(1 for c in calibrations if c['confidence'] == '-')

print(f'✓ 已写入 {len(calibrations)} 条标定数据')
print(f'  高置信度: {success}, 中置信度: {medium}, 低置信度: {low}, API错误跳过: {errors}')
print(f'  输出路径: {out_path}')

# 清理临时文件
import glob
for f in glob.glob('temp_scan_results*.json'):
    os.remove(f)
    print(f'  ✓ 已清理临时文件: {f}')
