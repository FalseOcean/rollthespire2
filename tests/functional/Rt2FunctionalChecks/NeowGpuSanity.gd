extends SceneTree

func bytes(values: Array) -> PackedByteArray:
	var data := PackedByteArray()
	data.resize(maxi(4, values.size()*4))
	for i in values.size(): data.encode_u32(i*4, int(values[i]))
	return data

func _initialize():
	var rd := RenderingServer.create_local_rendering_device()
	if rd == null:
		print("N_GPU_FAIL: no RenderingDevice")
		quit(1)
		return
	var shaders: Array[RID] = []
	var pipelines: Array[RID] = []
	for file in ["stage0.glsl","stage1.glsl","stage2.glsl","shader.glsl","nr.glsl","leafy-stage0.glsl","leafy-stage1.glsl","leafy-stage2.glsl","leafy-shader.glsl","bones-nr-stage0.glsl","bones-nr-stage1.glsl","bones-nr-stage2.glsl","bones-nr.glsl","bones-arcane-nr-stage0.glsl","bones-arcane-nr-stage1.glsl","bones-arcane-nr-stage2.glsl","bones-arcane-nr.glsl","stage0.glsl","stage1.glsl","stage2.glsl","shader.glsl","leafy-stage0.glsl","leafy-stage1.glsl","leafy-stage2.glsl","leafy-shader.glsl"]:
		var source := RDShaderSource.new()
		source.source_compute = FileAccess.get_file_as_string("res://"+file)
		if shaders.size() >= 17:
			source.source_compute = source.source_compute.replace("#define N_AUTHORED_UPGRADES 0", "#define N_AUTHORED_UPGRADES 1")
		var spirv := rd.shader_compile_spirv_from_source(source)
		if spirv.compile_error_compute != "":
			print("N_GPU_FAIL: ",file,spirv.compile_error_compute)
			quit(1)
			return
		var shader := rd.shader_create_from_spirv(spirv)
		shaders.append(shader)
		pipelines.append(rd.compute_pipeline_create(shader))
	var fixtures = JSON.parse_string(FileAccess.get_file_as_string("res://fixtures.json"))
	var failures := 0
	for f in fixtures:
		if f.get("timingOnly",false): continue
		var p = f.gpu
		var count := int(f.ordinals.size())
		var capacity := int(f.count)
		var staged: bool = not f.compact and int(p.Meta[66]) != 0
		var leafy: bool = int(p.Meta[70]) != 0
		var nr: bool = f.has("composite")
		var bones_nr: bool = f.get("bonesComposite",false)
		var arcane_nr: bool = f.get("bonesArcane",false)
		var single_stage := 16 if arcane_nr else (12 if bones_nr else (4 if nr else (8 if leafy else 3)))
		var initial = [[int(f.start),0,capacity,count,1 if f.compact else 0,capacity,capacity,capacity],p.Meta,p.PoolMeta,p.Cards,p.Strike,p.Defend,[0x4e464d52,1,0,0,0,123,0,0],[],p.Bones,p.Conditions,f.ordinals]
		if arcane_nr: initial[6].append_array([0,0,0,0])
		if staged or bones_nr: initial.append_array([[],[]])
		if nr: initial.append(f.composite)
		var buffers: Array[RID] = []
		var uniforms: Array[RDUniform] = []
		for i in initial.size():
			var data := bytes(initial[i])
			if i == 7: data.resize(capacity*4)
			if i == 11 and (not nr or bones_nr): data.resize(capacity*12)
			if i == 12: data.resize(capacity*48)
			var buffer := rd.storage_buffer_create(data.size(),data)
			buffers.append(buffer)
			var uniform := RDUniform.new()
			uniform.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
			uniform.binding = i
			uniform.add_id(buffer)
			uniforms.append(uniform)
		var bindings: Array[RID] = []
		var selected_stages: Array = (([13,14,15] if arcane_nr else ([9,10,11] if bones_nr else ([5,6,7] if leafy else [0,1,2]))) if staged else [single_stage])
		if p.Conditions.size() > 160 and not nr:
			selected_stages = ([21,22,23] if leafy else [17,18,19]) if staged else ([24] if leafy else [20])
		for stage in selected_stages: bindings.append(rd.uniform_set_create(uniforms,shaders[stage],0))
		var list := rd.compute_list_begin()
		for i in bindings.size():
			if i > 0: rd.compute_list_add_barrier(list)
			rd.compute_list_bind_compute_pipeline(list,pipelines[selected_stages[i]])
			rd.compute_list_bind_uniform_set(list,bindings[i],0)
			rd.compute_list_dispatch(list,ceili((count if i == 0 else capacity)/512.0),1,1)
		rd.compute_list_end()
		rd.submit()
		rd.sync()
		var header := rd.buffer_get_data(buffers[6])
		var survivors := int(header.decode_u32(8))
		var actual: Array[int] = []
		if survivors <= capacity:
			var result := rd.buffer_get_data(buffers[7])
			for i in survivors: actual.append(int(result.decode_u32(i*4)))
		actual.sort()
		var expected: Array[int] = []
		for value in f.expected: expected.append(int(value))
		var ok := actual == expected and header.decode_u32(16) == count and header.decode_u32(12) == 0
		if arcane_nr:
			var pair_pass := int(header.decode_u32(32))
			var arcane_pass := int(header.decode_u32(36))
			var rarity_pass := int(header.decode_u32(40))
			var heavy := int(header.decode_u32(44))
			ok = ok and pair_pass <= count and arcane_pass <= pair_pass and rarity_pass <= arcane_pass and survivors <= rarity_pass and heavy >= rarity_pass and heavy <= 2*rarity_pass
		if not ok: failures += 1
		print("N_GPU ",f.name," compact=",f.compact," staged=",staged," input=",count," survivors=",survivors," expected=",expected.size()," passed=",ok)
		if staged and f.name == "BonesNewLeafTransformFinalCurse.TwoEntryPool.False":
			for overflow_stage in [6,7]:
				var tiny_meta: Array = initial[0].duplicate()
				tiny_meta[overflow_stage] = 1
				var reset := bytes(initial[6])
				var tiny := bytes(tiny_meta)
				rd.buffer_update(buffers[0],0,tiny.size(),tiny)
				rd.buffer_update(buffers[6],0,reset.size(),reset)
				list = rd.compute_list_begin()
				for stage in 3:
					if stage > 0: rd.compute_list_add_barrier(list)
					rd.compute_list_bind_compute_pipeline(list,pipelines[selected_stages[stage]])
					rd.compute_list_bind_uniform_set(list,bindings[stage],0)
					rd.compute_list_dispatch(list,ceili(count/512.0),1,1)
				rd.compute_list_end()
				rd.submit()
				rd.sync()
				var overflow_header := rd.buffer_get_data(buffers[6])
				var overflow_ok := overflow_header.decode_u32(12) != 0 and overflow_header.decode_u32(16) == count
				if not overflow_ok: failures += 1
				print("N_GPU_OVERFLOW stage=",overflow_stage," signalled=",overflow_ok)
		for binding in bindings: rd.free_rid(binding)
		for buffer in buffers: rd.free_rid(buffer)
	for pipeline in pipelines: rd.free_rid(pipeline)
	for shader in shaders: rd.free_rid(shader)
	rd.free()
	print("N_GPU_SUMMARY fixtures=",fixtures.size()," failures=",failures)
	quit(0 if failures == 0 else 1)
