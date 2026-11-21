extends Node3D

# 用 SubViewport 渲染低分辨率画面，再抓图。
# 这同时解决了两个问题：
#   1. 截图可靠（SubViewport 有明确的 render target）
#   2. 低分辨率像素化（对应 Unity 里的 PixelRenderScale）

const RENDER_W := 480
const RENDER_H := 270

var _sub: SubViewport
var _dbg := false

# 递归给所有 MeshInstance3D 换材质
func _apply_material(node: Node, mat: Material) -> void:
	if node is MeshInstance3D:
		# 从原材质里取贴图，传给 shader（material_override 会丢掉原贴图）
		var tex: Texture2D = null
		var col := Color(0.35, 0.55, 0.28)
		var cur = node.get_active_material(0)
		if node.material_override == null and cur != null and not (cur is ShaderMaterial):
			if cur is BaseMaterial3D:
				tex = cur.albedo_texture
				col = cur.albedo_color
				if _dbg == false:
					_dbg = true
					print("[G] 原材质: ", cur.get_class(), "  有贴图=", tex != null, "  颜色=", col)
		var m: ShaderMaterial = mat.duplicate()
		m.set_shader_parameter("albedo_color", Vector3(col.r, col.g, col.b))
		if tex != null:
			m.set_shader_parameter("albedo_tex", tex)
		node.material_override = m
	for c in node.get_children():
		_apply_material(c, mat)


func _ready() -> void:
	# ---- SubViewport（低分辨率渲染目标）
	_sub = SubViewport.new()
	_sub.size = Vector2i(RENDER_W, RENDER_H)
	_sub.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	_sub.transparent_bg = false
	_sub.msaa_3d = Viewport.MSAA_DISABLED
	add_child(_sub)

	# ---- 3D 世界全部挂在 SubViewport 下
	var world := Node3D.new()
	_sub.add_child(world)

	# 地面
	var ground := MeshInstance3D.new()
	var plane := PlaneMesh.new()
	plane.size = Vector2(200, 200)
	ground.mesh = plane
	var gmat := StandardMaterial3D.new()
	gmat.albedo_color = Color(0.30, 0.45, 0.22)
	gmat.roughness = 1.0
	ground.material_override = gmat
	world.add_child(ground)

	# 树
	var packed = load("res://assets/trees/apple_tree.glb")
	if packed == null:
		print("[G] 树加载失败")
	else:
		var rng := RandomNumberGenerator.new()
		rng.seed = 20261122
		var placed := 0
		for i in range(50):
			var inst = packed.instantiate()
			inst.position = Vector3(rng.randf_range(-28.0, 28.0), 0.0, rng.randf_range(-28.0, 28.0))
			inst.rotate_y(rng.randf_range(0.0, TAU))
			var s := rng.randf_range(3.5, 5.5)
			inst.scale = Vector3(s, s, s)
			# 挂 toon shader（球形法线 + 卡通量化）
			var shader_mat := ShaderMaterial.new()
			shader_mat.shader = load("res://assets/shaders/tree_toon.gdshader")
			_apply_material(inst, shader_mat)
			world.add_child(inst)
			placed += 1
		print("[G] 摆了 ", placed, " 棵树")

	# 太阳
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-52, -30, 0)
	sun.light_energy = 1.4
	sun.shadow_enabled = true
	sun.directional_shadow_max_distance = 120.0
	# 阴影分辨率降下来（吃性能的是阴影，不是光照）
	sun.directional_shadow_mode = DirectionalLight3D.SHADOW_PARALLEL_2_SPLITS
	sun.shadow_bias = 0.03
	world.add_child(sun)

	# 环境
	var we := WorldEnvironment.new()
	var e := Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color(0.62, 0.72, 0.78)
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color(0.60, 0.66, 0.60)
	e.ambient_light_energy = 1.1
	e.fog_enabled = true
	e.fog_light_color = Color(0.62, 0.72, 0.78)
	e.fog_density = 0.0025
	we.environment = e
	world.add_child(we)

	# 相机
	var cam := Camera3D.new()
	cam.position = Vector3(0, 48, -46)
	cam.fov = 55.0
	cam.far = 600.0
	world.add_child(cam)
	cam.look_at(Vector3(0, 2, 0))
	cam.current = true

	# ---- 等若干帧让渲染稳定
	for i in range(8):
		await get_tree().process_frame
	await RenderingServer.frame_post_draw
	await RenderingServer.frame_post_draw

	var tex := _sub.get_texture()
	if tex == null:
		print("[G] SubViewport 没有 texture")
		get_tree().quit()
		return

	var img: Image = tex.get_image()
	if img == null:
		print("[G] get_image 返回 null")
		get_tree().quit()
		return

	# 统计平均色，确认不是纯色
	var w := img.get_width()
	var h := img.get_height()
	var step := 16
	var sum := Vector3.ZERO
	var n := 0
	for y in range(0, h, step):
		for x in range(0, w, step):
			var c := img.get_pixel(x, y)
			sum += Vector3(c.r, c.g, c.b)
			n += 1
	var avg: Vector3 = sum / float(max(1, n))
	print("[G] 渲染 ", w, "x", h, "  平均色 (", snappedf(avg.x, 0.01), ", ", snappedf(avg.y, 0.01), ", ", snappedf(avg.z, 0.01), ")")

	var err := img.save_png("res://godot_shot.png")
	print("[G] 截图保存 err=", err)
	get_tree().quit()
