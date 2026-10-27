extends SceneTree

func bytes(values: Array) -> PackedByteArray:
	var data := PackedByteArray()
	data.resize(maxi(4,values.size()*4))
	for i in values.size(): data.encode_u32(i*4,int(values[i]))
	return data

func _initialize():
	var rd := RenderingServer.create_local_rendering_device()
	if rd == null:
		print("C_GPU_FAIL: no device")
		quit(1)
		return
	var cache := {}
	var fixtures = JSON.parse_string(FileAccess.get_file_as_string("res://fixtures.json"))
	var failures := 0
	for f in fixtures:
		if not cache.has(f.shader):
			var source := RDShaderSource.new()
			source.source_compute = FileAccess.get_file_as_string("res://"+f.shader)
			var spirv := rd.shader_compile_spirv_from_source(source)
			if spirv.compile_error_compute != "":
				print("C_GPU_FAIL: ",f.shader,spirv.compile_error_compute)
				quit(1)
				return
			var shader := rd.shader_create_from_spirv(spirv)
			cache[f.shader] = [shader,rd.compute_pipeline_create(shader)]
		var compiled = cache[f.shader]
		var initial = [f.batchMeta,f.Meta,[0],f.Pools,f.Ids,[0],f.Predicates,f.Targets,[0x43464d52,1,0,0,0,0x43463031,0,0],[],f.ordinals]
		var buffers: Array[RID] = []
		var uniforms: Array[RDUniform] = []
		for i in initial.size():
			var data := bytes(initial[i])
			if i==9: data.resize(int(f.batchMeta[5])*4)
			var buffer := rd.storage_buffer_create(data.size(),data)
			buffers.append(buffer)
			var uniform := RDUniform.new()
			uniform.uniform_type=RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
			uniform.binding=i
			uniform.add_id(buffer)
			uniforms.append(uniform)
		var binding := rd.uniform_set_create(uniforms,compiled[0],0)
		var list := rd.compute_list_begin()
		rd.compute_list_bind_compute_pipeline(list,compiled[1])
		rd.compute_list_bind_uniform_set(list,binding,0)
		rd.compute_list_dispatch(list,ceili(float(f.batchMeta[3])/64.0),1,1)
		rd.compute_list_end()
		rd.submit()
		rd.sync()
		var header := rd.buffer_get_data(buffers[8])
		var count := int(header.decode_u32(8))
		var actual: Array[int] = []
		if count>0 and count<=int(f.batchMeta[5]):
			var payload := rd.buffer_get_data(buffers[9],0,count*4)
			for i in count: actual.append(payload.decode_u32(i*4))
		actual.sort()
		var expected: Array[int] = []
		for i in f.expected: expected.append(int(i))
		var expected_fault: bool = f.get("expectedFault",false)
		var fault_ok: bool = header.decode_u32(12)==(1 if expected_fault else 0)
		var ok: bool = fault_ok and header.decode_u32(16)==int(f.batchMeta[3]) and (expected_fault or actual==expected)
		if not ok: failures+=1
		print("C_GPU_", "PASS" if ok else "FAIL", ": ",f.name," compact=",f.batchMeta[4]," baseHi=",f.batchMeta[1]," input=",f.batchMeta[3]," output=",count," expected=",expected.size()," fault=",header.decode_u32(12))
		rd.free_rid(binding)
		for buffer in buffers: rd.free_rid(buffer)
	for compiled in cache.values():
		rd.free_rid(compiled[1])
		rd.free_rid(compiled[0])
	rd.free()
	print("C_GPU_SUMMARY fixtures=",fixtures.size()," failures=",failures)
	quit(0 if failures==0 else 1)
