﻿using UnityEngine;
using Random = System.Random;

/*TODO: Unity does not support drawIndexed...
 Either use a workaround:
 	- Move culled Vertices in ComputeShader to a position outside viewfrustum for hardware culling after vertexshader
 	- in vertex shader use vertexID as index to buffers. If it was culled it gets moved outside viewfrustm.
 Or:
 	- Use nativePlugin ... TODO... can i use DrawIndexed there? I probably can.
 */

namespace GrassSimulation.LOD
{
	public class GrassPatch : Patch, IDestroyable
	{
		private readonly uint[] _args = new uint[5] {0, 0, 0, 0, 0};
		private readonly MaterialPropertyBlock _materialPropertyBlock;
		private readonly Matrix4x4 _matrix;
		private readonly float[] _matrixAsFloats;
		private readonly Material _ownMaterial;
		private readonly Vector4 _patchTexCoord; //x: xStart, y: yStart, z: width, w:height
		private readonly Random _random;
		private readonly int _startIndex;
		private ComputeBuffer _argsBuffer;
		private int _currentAmountBlades;
		private Mesh _dummyMesh;
		private Matrix4x4 _gizmoMatrix;
		private Vector4[] _grassDataA; //xyz: upVector, w: pos.y
		private ComputeBuffer _grassDataABuffer;
		private Vector4[] _grassDataB; //xyz: v1, w: height
		private ComputeBuffer _grassDataBBuffer;
		private Vector4[] _grassDataC; //xyz: v2, w: dirAlpha
		private ComputeBuffer _grassDataCBuffer;
		private ComputeShader _visibilityShader;

		public GrassPatch(SimulationContext context, Vector4 patchTexCoord, Bounds bounds) : base(context)
		{
			Bounds = bounds;
			_patchTexCoord = patchTexCoord;
			_startIndex =
				(int) UnityEngine.Random.Range(0,
					Context.Settings.GetAmountPrecomputedBlades() - Context.Settings.GetAmountBlades() - 1);
			_random = new Random(Context.Settings.RandomSeed);
			var translateGizmo = bounds.center - bounds.extents;
			translateGizmo.y = 0;
			var translate = new Vector3(-bounds.extents.x, -bounds.center.y, -bounds.extents.z);
			//translate.y = -bounds.center.y;
			_matrix = Matrix4x4.TRS(translate, Quaternion.identity,
				new Vector3(Context.Settings.PatchSize, 1, Context.Settings.PatchSize));
			_gizmoMatrix = Matrix4x4.TRS(translateGizmo, Quaternion.identity,
				new Vector3(Context.Settings.PatchSize, 1, Context.Settings.PatchSize));
			_matrixAsFloats = new[]
			{
				_matrix.m00, _matrix.m01, _matrix.m02, _matrix.m03,
				_matrix.m10, _matrix.m11, _matrix.m12, _matrix.m13,
				_matrix.m20, _matrix.m21, _matrix.m22, _matrix.m23,
				_matrix.m30, _matrix.m31, _matrix.m32, _matrix.m33
			};
			_materialPropertyBlock = new MaterialPropertyBlock();
			_ownMaterial = new Material(Context.GrassSimulationShader);
			
		
			CreatePerBladeData();
			CreateDummyMesh();
			SetupComputeBuffers();
			SetupMaterialPropertyBlock();
		}

		public override bool IsLeaf
		{
			get { return true; }
		}

		public void Destroy()
		{
			_argsBuffer.Release();
			_grassDataABuffer.Release();
			_grassDataBBuffer.Release();
			_grassDataCBuffer.Release();
		}

		private void CreatePerBladeData()
		{
			_grassDataA = new Vector4[Context.Settings.GetAmountBlades()];
			_grassDataB = new Vector4[Context.Settings.GetAmountBlades()];
			_grassDataC = new Vector4[Context.Settings.GetAmountBlades()];
			for (var i = 0; i < Context.Settings.GetAmountBlades(); i++)
			{
				//Fill _grassDataA
				var bladePosition =
					new Vector2(_patchTexCoord.x + _patchTexCoord.z * Context.SharedGrassData.GrassData[_startIndex + i].x,
						_patchTexCoord.y + _patchTexCoord.w * Context.SharedGrassData.GrassData[_startIndex + i].y);
				var posY = Context.Heightmap.GetPixel((int) (bladePosition.x * Context.Heightmap.width),
					(int) (bladePosition.y * Context.Heightmap.height)).r;
				var up = Context.Terrain.terrainData.GetInterpolatedNormal(bladePosition.x, bladePosition.y);
				_grassDataA[i].Set(up.x, up.y, up.z, Context.Transform.position.y + posY * Context.Terrain.terrainData.size.y);
				//Fill _grassDataB
				var height = (float) (Context.Settings.BladeMinHeight +
				                      _random.NextDouble() * (Context.Settings.BladeMaxHeight - Context.Settings.BladeMinHeight));
				_grassDataB[i].Set(up.x * height / 2, up.y * height / 2, up.z * height / 2, height);
				//Fill _grassDataC
				var dirAlpha = (float) (_random.NextDouble() * Mathf.PI * 2f);
				_grassDataC[i].Set(up.x * height, up.y * height, up.z * height, dirAlpha);
			}
		}

		private void CreateDummyMesh()
		{
			var dummyMeshSize = Context.Settings.GetDummyMeshSize();
			var dummyVertices = new Vector3[dummyMeshSize];
			var indices = new int[dummyMeshSize];

			for (var i = 0; i < dummyMeshSize; i++)
			{
				dummyVertices[i] = Vector3.zero;
				indices[i] = i;
			}

			_dummyMesh = new Mesh();
			_dummyMesh.vertices = dummyVertices;
			_dummyMesh.SetIndices(indices, MeshTopology.Points, 0);
			_dummyMesh.RecalculateBounds();
		}

		private void SetupComputeBuffers()
		{
			_grassDataABuffer = new ComputeBuffer(_grassDataA.Length, 16, ComputeBufferType.Default);
			_grassDataBBuffer = new ComputeBuffer(_grassDataB.Length, 16, ComputeBufferType.Default);
			_grassDataCBuffer = new ComputeBuffer(_grassDataC.Length, 16, ComputeBufferType.Default);
			_grassDataABuffer.SetData(_grassDataA);
			_grassDataBBuffer.SetData(_grassDataB);
			_grassDataCBuffer.SetData(_grassDataC);
			_argsBuffer = new ComputeBuffer(1, _args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
			_args[0] = Context.Settings.GetDummyMeshSize();
			_args[1] = (uint) Context.Settings.GrassDensity;
			_argsBuffer.SetData(_args);
		}

		private void SetupMaterialPropertyBlock()
		{
			_materialPropertyBlock.SetFloat("startIndex", _startIndex);
			_materialPropertyBlock.SetBuffer("SharedGrassData", Context.SharedGrassData.SharedGrassBuffer);
			_materialPropertyBlock.SetBuffer("grassDataA", _grassDataABuffer);
			_materialPropertyBlock.SetBuffer("grassDataB", _grassDataBBuffer);
			_materialPropertyBlock.SetBuffer("grassDataC", _grassDataCBuffer);
			_materialPropertyBlock.SetMatrix("patchMatrix", _matrix);

			_ownMaterial.SetFloat("startIndex", _startIndex);
			_ownMaterial.SetBuffer("SharedGrassData", Context.SharedGrassData.SharedGrassBuffer);
			_ownMaterial.SetBuffer("grassDataA", _grassDataABuffer);
			_ownMaterial.SetBuffer("grassDataB", _grassDataBBuffer);
			_ownMaterial.SetBuffer("grassDataC", _grassDataCBuffer);
			_ownMaterial.SetMatrix("patchMatrix", _matrix);
		}

		public void UpdateForces()
		{
			Context.ForcesComputeShader.SetInt("startIndex", _startIndex);
			Context.ForcesComputeShader.SetFloats("patchMatrix", _matrixAsFloats);
			Context.ForcesComputeShader.SetInt("currentAmountBlades", _currentAmountBlades);
			
			Context.ForcesComputeShader.SetBuffer(Context.ForcesComputeShaderKernel, "grassDataA", _grassDataABuffer);
			Context.ForcesComputeShader.SetBuffer(Context.ForcesComputeShaderKernel, "grassDataB", _grassDataBBuffer);
			Context.ForcesComputeShader.SetBuffer(Context.ForcesComputeShaderKernel, "grassDataC", _grassDataCBuffer);

			Context.ForcesComputeShader.Dispatch(Context.ForcesComputeShaderKernel, (int) Context.Settings.GrassDensity, 0, 0);
		}

		public void UpdateVisibility()
		{
			Context.VisibilityComputeShader.SetInt("startIndex", _startIndex);
			Context.VisibilityComputeShader.SetFloats("patchMatrix", _matrixAsFloats);
			Context.VisibilityComputeShader.SetInt("currentAmountBlades", _currentAmountBlades);
			Context.VisibilityComputeShader.SetBuffer(Context.VisibilityComputeShaderKernel, "SharedGrassData", Context.SharedGrassData.SharedGrassBuffer);
			Context.VisibilityComputeShader.SetBuffer(Context.VisibilityComputeShaderKernel, "grassDataA", _grassDataABuffer);
			Context.VisibilityComputeShader.SetBuffer(Context.VisibilityComputeShaderKernel, "grassDataB", _grassDataBBuffer);
			Context.VisibilityComputeShader.SetBuffer(Context.VisibilityComputeShaderKernel, "grassDataC", _grassDataCBuffer);

			Context.VisibilityComputeShader.Dispatch(Context.VisibilityComputeShaderKernel, (int) Context.Settings.GrassDensity,
				1, 1);
		}

		public void Draw()
		{
			//_currentAmountBlades = (int) Context.Settings.GetAmountBlades();

			//UpdateForces();
			UpdateVisibility();
			SetupMaterialPropertyBlock();
			
			//Context.GrassSimulationMaterial.SetPass(0);

			//Context.GrassSimulationMaterial.SetFloat("startIndex", _startIndex);
			//Context.GrassSimulationMaterial.SetFloat("startIndex", _startIndex);
			//Context.GrassSimulationMaterial.SetMatrix("patchMatrix", _matrix);
			//Context.GrassSimulationMaterial.SetInt("currentAmountBlades", _currentAmountBlades);
			/*Context.GrassSimulationMaterial.SetBuffer("SharedGrassData", Context.SharedGrassData.SharedGrassBuffer);
			Context.GrassSimulationMaterial.SetBuffer("grassDataA", _grassDataABuffer);
			Context.GrassSimulationMaterial.SetBuffer("grassDataB", _grassDataABuffer);
			Context.GrassSimulationMaterial.SetBuffer("grassDataC", _grassDataABuffer);*/
			//Context.GrassSimulationMaterial

			//Graphics.DrawProcedural(MeshTopology.Points, _currentAmountBlades);
			//public static void DrawMeshInstancedIndirect(Mesh mesh, int submeshIndex, Material material, 
			//Bounds bounds, ComputeBuffer bufferWithArgs, int argsOffset = 0, 
			//MaterialPropertyBlock properties = null, Rendering.ShadowCastingMode castShadows = ShadowCastingMode.On, 
			//bool receiveShadows = true, int layer = 0, Camera camera = null); 
			//Graphics.DrawMeshInstancedIndirect(_dummyMesh, 0, Context.GrassSimulationMaterial, Bounds, _argsBuffer, 0, _materialPropertyBlock);
			Graphics.DrawMeshInstancedIndirect(_dummyMesh, 0, _ownMaterial, Bounds, _argsBuffer, 0);
		}

		public override void DrawGizmo()
		{
			if (Context.EditorSettings.DrawGrassPatchGizmo)
			{
				Gizmos.color = new Color(0f, 0f, 1f, 0.5f);
				Gizmos.DrawWireSphere(Bounds.center, 0.5f);
				Gizmos.DrawWireCube(Bounds.center, Bounds.size);
			}
			if (Context.EditorSettings.DrawGrassDataGizmo)
			{
				Gizmos.color = new Color(0f, 1f, 0f, 0.8f);
				for (var i = 0; i < Context.Settings.GetAmountBlades(); i++)
				{
					var pos = new Vector3(Context.SharedGrassData.GrassData[_startIndex + i].x,
						_grassDataA[i].w, Context.SharedGrassData.GrassData[_startIndex + i].y);
					pos = _gizmoMatrix.MultiplyPoint3x4(pos);
					Gizmos.DrawLine(pos, pos + new Vector3(_grassDataB[i].x, _grassDataB[i].y, _grassDataB[i].z));
				}
			}
		}
	}
}