extends SceneTree

func bytes(values: Array) -> PackedByteArray:
	var data := PackedByteArray()
	data.resize(maxi(4, values.size() * 4))
	for i in values.size(): data.encode_u32(i * 4, int(values[i]))
	return data

func _initialize():
	var rd := RenderingServer.create_local_rendering_device()
	if rd == null:
		print("RFULL_GPU_FAIL: no RenderingDevice")
		quit(1)
		return
	var source := RDShaderSource.new()
	source.source_compute = FileAccess.get_file_as_string("res://rfull.glsl")
	var spirv := rd.shader_compile_spirv_from_source(source)
	if spirv.compile_error_compute != "":
		print("RFULL_GPU_FAIL: ", spirv.compile_error_compute)
		quit(1)
		return
	var shader := rd.shader_create_from_spirv(spirv)
	var pipeline := rd.compute_pipeline_create(shader)
	var fixtures = JSON.parse_string(FileAccess.get_file_as_string("res://rfull-fixtures.json"))
	var failures := 0
	for f in fixtures:
		var input_count := int(f.ordinals.size())
		var capacity := int(f.count)
		var initial: Array = []
		initial.append([int(f.start), 0, capacity, input_count, 1 if f.compact else 0, capacity])
		initial.append(f.ordinals)
		for index in 7: initial.append(f.buffers[index])
		initial.append([0x52464d52, 1, 0, 0, 0, 123, 0, 0])
		initial.append([])
		for index in range(7, 10): initial.append(f.buffers[index])
		var buffers: Array[RID] = []
		var uniforms: Array[RDUniform] = []
		for binding in initial.size():
			var data := bytes(initial[binding])
			if binding == 10: data.resize(capacity * 4)
			var buffer := rd.storage_buffer_create(data.size(), data)
			buffers.append(buffer)
			var uniform := RDUniform.new()
			uniform.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
			uniform.binding = binding
			uniform.add_id(buffer)
			uniforms.append(uniform)
		var uniform_set := rd.uniform_set_create(uniforms, shader, 0)
		var list := rd.compute_list_begin()
		rd.compute_list_bind_compute_pipeline(list, pipeline)
		rd.compute_list_bind_uniform_set(list, uniform_set, 0)
		rd.compute_list_dispatch(list, ceili(input_count / 512.0), 1, 1)
		rd.compute_list_end()
		rd.submit()
		rd.sync()
		var header := rd.buffer_get_data(buffers[9])
		var survivor_count := int(header.decode_u32(8))
		var actual: Array[int] = []
		if survivor_count <= capacity:
			var result := rd.buffer_get_data(buffers[10])
			for index in survivor_count: actual.append(int(result.decode_u32(index * 4)))
		actual.sort()
		var expected: Array[int] = []
		for value in f.expected: expected.append(int(value))
		var passed := actual == expected and header.decode_u32(16) == input_count and header.decode_u32(12) == 0
		if not passed: failures += 1
		print("RFULL_GPU ", f.name, " compact=", f.compact, " input=", input_count,
			" survivors=", survivor_count, " expected=", expected.size(), " passed=", passed)
		rd.free_rid(uniform_set)
		for buffer in buffers: rd.free_rid(buffer)
	rd.free_rid(pipeline)
	rd.free_rid(shader)
	rd.free()
	print("RFULL_GPU_SUMMARY fixtures=", fixtures.size(), " failures=", failures)
	quit(0 if failures == 0 else 1)
