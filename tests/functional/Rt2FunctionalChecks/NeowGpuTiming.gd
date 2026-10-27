extends SceneTree
# Bounded before/after diagnostic; never feeds Production calibration.
func bytes(values: Array) -> PackedByteArray:
	var data := PackedByteArray()
	data.resize(maxi(4,values.size()*4))
	for i in values.size(): data.encode_u32(i*4,int(values[i]))
	return data

func _initialize():
	var rd := RenderingServer.create_local_rendering_device()
	var fixtures = JSON.parse_string(FileAccess.get_file_as_string("res://fixtures.json"))
	for file in ["before.glsl","shader.glsl","staged"]:
		var shaders: Array[RID] = []
		var pipelines: Array[RID] = []
		for path in (["stage0.glsl","stage1.glsl","stage2.glsl"] if file == "staged" else [file]):
			var source := RDShaderSource.new()
			source.source_compute = FileAccess.get_file_as_string("res://"+path)
			var spirv := rd.shader_compile_spirv_from_source(source)
			if spirv.compile_error_compute != "":
				print(spirv.compile_error_compute)
				quit(1)
				return
			var shader := rd.shader_create_from_spirv(spirv)
			shaders.append(shader)
			pipelines.append(rd.compute_pipeline_create(shader))
		for f in fixtures:
			if f.compact or f.name not in ["BonesUnorderedPair","BonesKaleidoscopeAndRelic","BonesNewLeafTransformFinalCurse"]: continue
			var p = f.gpu
			if file == "staged" and int(p.Meta[66]) == 0: continue
			var count := 16777216
			var initial = [[0,0,count,count,0,count,4194304,65536],p.Meta,p.PoolMeta,p.Cards,p.Strike,p.Defend,[0x4e464d52,1,0,0,0,123,0,0],[],p.Bones,p.Conditions,[]]
			if file == "staged": initial.append_array([[],[]])
			var buffers: Array[RID] = []
			var uniforms: Array[RDUniform] = []
			for i in initial.size():
				var data := bytes(initial[i])
				if i == 7: data.resize(count*4)
				if i == 11: data.resize(4194304*12)
				if i == 12: data.resize(65536*48)
				var buffer := rd.storage_buffer_create(data.size(),data)
				buffers.append(buffer)
				var uniform := RDUniform.new()
				uniform.uniform_type = RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER
				uniform.binding = i
				uniform.add_id(buffer)
				uniforms.append(uniform)
			var bindings: Array[RID] = []
			for shader in shaders: bindings.append(rd.uniform_set_create(uniforms,shader,0))
			for iteration in 6:
				var start := Time.get_ticks_usec()
				var batch := bytes([iteration*count,0,count,count,0,count,4194304,65536])
				var header := bytes(initial[6])
				rd.buffer_update(buffers[0],0,batch.size(),batch)
				rd.buffer_update(buffers[6],0,header.size(),header)
				var uploaded := Time.get_ticks_usec()
				var list := rd.compute_list_begin()
				for stage in pipelines.size():
					if stage > 0: rd.compute_list_add_barrier(list)
					rd.compute_list_bind_compute_pipeline(list,pipelines[stage])
					rd.compute_list_bind_uniform_set(list,bindings[stage],0)
					rd.compute_list_dispatch(list,[count/512,8192,128][stage],1,1)
				rd.compute_list_end()
				rd.submit()
				rd.sync()
				var synced := Time.get_ticks_usec()
				header = rd.buffer_get_data(buffers[6])
				var survivors := int(header.decode_u32(8))
				var ordinals := PackedInt32Array()
				if survivors > 0: ordinals = rd.buffer_get_data(buffers[7],0,survivors*4).to_int32_array()
				var read := Time.get_ticks_usec()
				ordinals.sort()
				var end := Time.get_ticks_usec()
				print("N_TIMING file=",file," shape=",f.name," iteration=",iteration," survivors=",survivors," uploadUs=",uploaded-start," dispatchSyncUs=",synced-uploaded," readUs=",read-synced," sortUs=",end-read," totalUs=",end-start)
			for binding in bindings: rd.free_rid(binding)
			for buffer in buffers: rd.free_rid(buffer)
		for pipeline in pipelines: rd.free_rid(pipeline)
		for shader in shaders: rd.free_rid(shader)
	rd.free()
	quit(0)
