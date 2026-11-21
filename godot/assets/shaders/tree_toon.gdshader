shader_type spatial;
render_mode diffuse_toon, specular_disabled, cull_disabled;

// 球形法线重映射 + 卡通光照量化
//
// 原理（借鉴 t3ssel8r）：
//   树叶是"卡片堆"，每张卡片法线朝自己，光照下就是一堆互不相干的平面。
//   把每个顶点的法线改成「从树冠中心指向该顶点」——
//   整棵树冠就变成一个球，光照连续，立刻有体积感。
//
//   这不是换模型，只是改法线方向。

// 树冠中心（模型局部空间）
uniform vec3 canopy_center = vec3(0.0, 1.35, 0.0);
// 球形法线权重：1=全用球形法线，0=用原法线
uniform float spherical_blend : hint_range(0.0, 1.0) = 1.0;

// 卡通量化档数
uniform int light_steps : hint_range(2, 6) = 3;
// 量化柔和度（0=硬边，越大越柔）
uniform float step_softness : hint_range(0.0, 0.5) = 0.02;

// 颜色
uniform vec3 shadow_tint : source_color = vec3(0.42, 0.52, 0.46);
uniform vec3 light_tint : source_color = vec3(1.0, 0.98, 0.90);
uniform float albedo_boost = 1.0;
uniform sampler2D albedo_tex : source_color, filter_nearest;
uniform vec3 albedo_color : source_color = vec3(0.35, 0.55, 0.28);

// 风摆
uniform float wind_strength : hint_range(0.0, 1.0) = 0.10;
uniform float wind_speed = 1.4;
uniform float leaf_jitter : hint_range(0.0, 0.1) = 0.02;

varying vec3 v_world_pos;

void vertex() {
	// ---- 球形法线重映射（核心）
	vec3 spherical = normalize(VERTEX - canopy_center);
	NORMAL = normalize(mix(NORMAL, spherical, spherical_blend));

	// ---- 风摆：用高度做权重，根不动顶摆
	float h = clamp((VERTEX.y - canopy_center.y * 0.4) / max(0.001, canopy_center.y), 0.0, 1.0);
	h = h * h;   // 平方让根部更稳

	// 低频整树摇
	float sway = sin(TIME * wind_speed + VERTEX.x * 0.9 + VERTEX.z * 0.7);
	VERTEX.x += sway * wind_strength * h;
	VERTEX.z += sway * wind_strength * 0.7 * h;

	// 高频叶子抖（沿法线方向微动）
	float jitter = sin(TIME * 6.5 + VERTEX.x * 7.0 + VERTEX.z * 5.0);
	VERTEX += NORMAL * jitter * leaf_jitter * h;

	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec4 tex = texture(albedo_tex, UV);
	// 贴图 alpha 为 0 的地方丢弃（叶片卡片留空）
	if (tex.a < 0.35) { discard; }
	ALBEDO = tex.rgb * albedo_color * albedo_boost * COLOR.rgb;
	ROUGHNESS = 1.0;
	SPECULAR = 0.0;
}

void light() {
	// ---- 卡通光照量化
	float ndl = dot(NORMAL, LIGHT);

	// 量化成 N 档，用 smoothstep 做软边
	float steps = float(light_steps);
	float q = floor(ndl * steps) / steps;
	// 在每档边界做一点点过渡，避免锯齿
	float frac_part = fract(ndl * steps);
	float soft = smoothstep(0.5 - step_softness, 0.5 + step_softness, frac_part);
	q = (floor(ndl * steps) + soft) / steps;

	q = clamp(q, 0.0, 1.0);

	// 阴影侧不用纯黑，用冷色调（t3ssel8r 的做法）
	vec3 tinted = mix(shadow_tint, light_tint, q);

	// 必须乘 ALBEDO，否则光照直接加成白色
	DIFFUSE_LIGHT += ALBEDO * tinted * ATTENUATION * LIGHT_COLOR;
}
