using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using NaughtyAttributes;
using System;

public class PolygonMapDebugView : MonoBehaviour
{
	[Flags] //This attribute turns the enum into a bitmask, and Unity has a special inspactor for bitmasks. We use a bitmask so we can draw more modes at once. Since this is a int, we can have up to 32 values
	public enum ViewModes
	{
		VoronoiCells		= 1 << 0, //Bit shifts the bit value of "1" (something like 0001, but with 32 digits, since this is a int) 0 bit to the left
		VoronoiEdges		= 1 << 1, //Same as above, but shifting 1 bit to the left, so the result will be "0010" (which is 2 in Decimal)
		VoronoiCorners		= 1 << 2,
		DelaunayEdges		= 1 << 3,
		DelaunayCorners		= 1 << 4,
		Neighboor			= 1 << 5,
		Noise				= 1 << 6,
		Borders				= 1 << 7,
		Water				= 1 << 8,
	}

    public PolygonMap generator;
	public RawImage rend;
	public Vector2Int resolution = new Vector2Int(512, 512);
	public ComputeShader computeShader;
	[Header("Options")]
	public ViewModes views;
	public int neighboorID = -1;

	private Color[,] texColors;
	private RenderTexture rt;
	private int buildTextureKernelIndex;
	private int findClosestCellKernelIndex;

	private const int POINT_SIZE = 5;

	private struct ColorData
	{
		public Vector2Int position;
		public Color color;
	}

	private struct CellData
	{
		public Vector2Int position;
	}

	private void Awake()
	{
		//Create a render texture and enable random write so we can rend things to it
		rt = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.ARGB32);
		rt.enableRandomWrite = true;
		rt.Create();

		//Set the texture for the renderer
		rend.texture = rt;

		//Get the kernel IDs
		buildTextureKernelIndex = computeShader.FindKernel("GenerateTexture");
		findClosestCellKernelIndex = computeShader.FindKernel("FindClosestCell");

		//Set the texture for the shader
		computeShader.SetTexture(buildTextureKernelIndex, "_Result", rt);
	}

	private void OnEnable()
	{
		if(generator != null)
		{
			generator.onMapGenerated += GenerateDebugTexture;
		}
	}

	private void OnDisable()
	{
		if (generator != null)
		{
			generator.onMapGenerated -= GenerateDebugTexture;
		}

		rt?.Release();
	}

	#region Generations
	[Button]
	private void GenerateDebugTexture()
	{
		if (!Application.isPlaying)
		{
			Debug.LogWarning("Only Available in Play Mode");
			return;
		}

		if (rend == null || generator == null)
			return;

		texColors = new Color[resolution.x, resolution.y];

		//Draw the debug info into the texture. The order here defines the draw order

		//BACKGROUND
		if ((views & ViewModes.VoronoiCells) != 0)
			DrawVoronoiCells();
		else if ((views & ViewModes.Noise) != 0)
			DrawNoise();
		else if ((views & ViewModes.Water) != 0)
			DrawWater();

		//OVERLAYS
		if ((views & ViewModes.VoronoiEdges) != 0) //Here we "create" a new byte value by comparing "mode" with "voronoiCells" using a "&" (AND) bitwise operator. As an example, the comparision works like this: 0011 & 0110 = 0010. Then we compare with "0", since zero is "0000".
			DrawVoronoiEdges();

		if ((views & ViewModes.VoronoiCorners) != 0)
			DrawVoronoiCorners();

		if ((views & ViewModes.DelaunayEdges) != 0)
			DrawDelaunayEdges();

		if ((views & ViewModes.DelaunayCorners) != 0)
			DrawDelaunayCorners();

		if ((views & ViewModes.Neighboor) != 0)
			DrawNeighboors();

		if ((views & ViewModes.Borders) != 0)
			DrawMapBorders();

		//Create the texture and assign the texture using a Helper Compute Shader
		ApplyChangesToTexture();
	}
	#endregion

	#region Draw Views
	private void DrawVoronoiCells()
	{
		int[] cellIDs = GetClosestCenterForPixels();

		for (int x = 0; x < resolution.x; x++)
		{
			for (int y = 0; y < resolution.y; y++)
			{
				float value = cellIDs[x + y * resolution.y] / (float)generator.delaunayCenters.Count;
				texColors[x, y] = new Color(value, value, value);
			}
		}
	}

	private void DrawVoronoiEdges()
	{
		foreach (var edge in generator.mapEdges)
		{
			Vector2Int pos0 = MapGraphCoordToTextureCoords(edge.v0.position.x, edge.v0.position.y);
			Vector2Int pos1 = MapGraphCoordToTextureCoords(edge.v1.position.x, edge.v1.position.y);
			DrawLine(pos0.x, pos0.y, pos1.x, pos1.y, 1, Color.white);
		}
	}

	private void DrawVoronoiCorners()
	{
		foreach (var corner in generator.voronoiCorners)
		{
			Vector2Int pos = MapGraphCoordToTextureCoords(corner.position.x, corner.position.y);
			DrawCircle(pos.x, pos.y, POINT_SIZE, Color.blue);
		}
	}

	private void DrawDelaunayEdges()
	{
		foreach (var edge in generator.mapEdges)
		{
			Vector2Int pos0 = MapGraphCoordToTextureCoords(edge.d0.position.x, edge.d0.position.y);
			Vector2Int pos1 = MapGraphCoordToTextureCoords(edge.d1.position.x, edge.d1.position.y);
			DrawLine(pos0.x, pos0.y, pos1.x, pos1.y, 1, Color.black);
		}
	}

	private void DrawDelaunayCorners()
	{
		foreach (var center in generator.delaunayCenters)
		{
			Vector2Int pos = MapGraphCoordToTextureCoords(center.position.x, center.position.y);
			DrawCircle(pos.x, pos.y, POINT_SIZE, Color.red);
		}
	}

	private void DrawNeighboors()
	{
		CellCenter c = generator.delaunayCenters[neighboorID];

		Vector2Int pos = MapGraphCoordToTextureCoords(c.position.x, c.position.y);
		DrawWireCircle(pos.x, pos.y, POINT_SIZE * 3, 2, Color.green);

		for (int i = 0; i < c.neighborCells.Count; i++)
		{
			pos = MapGraphCoordToTextureCoords(c.neighborCells[i].position.x, c.neighborCells[i].position.y);
			DrawWireCircle(pos.x, pos.y, POINT_SIZE * 2, 2, Color.magenta);
		}

		for (int i = 0; i < c.cellCorners.Count; i++)
		{
			pos = MapGraphCoordToTextureCoords(c.cellCorners[i].position.x, c.cellCorners[i].position.y);
			DrawWireCircle(pos.x, pos.y, POINT_SIZE * 2, 2, Color.yellow);
		}

		for (int i = 0; i < c.borderEdges.Count; i++)
		{
			Vector2Int pos0 = MapGraphCoordToTextureCoords(c.borderEdges[i].d0.position.x, c.borderEdges[i].d0.position.y);
			Vector2Int pos1 = MapGraphCoordToTextureCoords(c.borderEdges[i].d1.position.x, c.borderEdges[i].d1.position.y);
			DrawLine(pos0.x, pos0.y, pos1.x, pos1.y, 1, new Color(0, .5f, .5f));

			pos0 = MapGraphCoordToTextureCoords(c.borderEdges[i].v0.position.x, c.borderEdges[i].v0.position.y);
			pos1 = MapGraphCoordToTextureCoords(c.borderEdges[i].v1.position.x, c.borderEdges[i].v1.position.y);
			DrawLine(pos0.x, pos0.y, pos1.x, pos1.y, 1, Color.cyan);
		}
	}

	private void DrawMapBorders()
	{
		foreach (var corner in generator.voronoiCorners)
		{
			if (corner.isBorder)
			{
				Vector2Int pos = MapGraphCoordToTextureCoords(corner.position.x, corner.position.y);
				DrawSquare(pos.x, pos.y, POINT_SIZE * 2, Color.red);
			}
		}

		foreach (var center in generator.delaunayCenters)
		{
			if (center.isBorder)
			{
				Vector2Int pos = MapGraphCoordToTextureCoords(center.position.x, center.position.y);
				DrawWireSquare(pos.x, pos.y, POINT_SIZE * 2, 2, Color.red);
			}
		}
	}

	private void DrawNoise()
	{
		for (int x = 0; x < resolution.x; x++)
		{
			for (int y = 0; y < resolution.y; y++)
			{
				Vector2 normalizedPos = new Vector2() {
					x = ((x / (float)resolution.x) - 0.5f) * 2,
					y = ((y / (float)resolution.y) - 0.5f) * 2
				};

				Vector2 perlinPos = new Vector2() {
					x = (normalizedPos.x * 0.5f + 0.5f) * generator.size.x,
					y = (normalizedPos.y * 0.5f + 0.5f) * generator.size.y
				};

				float value = Mathf.PerlinNoise(perlinPos.x * generator.noiseSize + generator.noiseSeed, perlinPos.y * generator.noiseSize + generator.noiseSeed);
				float value2 = Mathf.Pow(normalizedPos.magnitude, generator.borderSize);

				texColors[x, y] = value > value2 ? Color.gray : Color.black;
			}
		}
	}

	private void DrawWater()
	{
		Color water = Color.blue;
		Color land = Color.white;

		//Paint the cells
		for (int x = 0; x < resolution.x; x++)
		{
			for (int y = 0; y < resolution.y; y++)
			{
				Vector2 pos = MapTextureCoordToGraphCoords(x, y);
				CellCenter c = GetClosestCenterFromPoint(pos);

				if (c != null)
				{
					texColors[x, y] = c.isWater ? water : land;
				}
			}
		}
	}
	#endregion

	#region Helper Functions
	private void ApplyChangesToTexture()
	{
		//Create a new Buffer
		ComputeBuffer shaderBuffer = new ComputeBuffer(resolution.x * resolution.y, sizeof(float) * 4 + sizeof(int) * 2); //4 because a color has 4 channels, 2 because the position has X and Y
		ColorData[] colorData = new ColorData[resolution.x * resolution.y];

		for (int x = 0; x < resolution.x; x++)
		{
			for (int y = 0; y < resolution.y; y++)
			{
				ColorData data = new ColorData() {
					position = new Vector2Int(x, y),
					color = texColors[x, y]
				};

				colorData[x + y * resolution.y] = data;
			}
		}

		shaderBuffer.SetData(colorData);
		computeShader.SetBuffer(buildTextureKernelIndex, "_ColorData", shaderBuffer);
		computeShader.SetInt("_Resolution", resolution.x);

		computeShader.Dispatch(buildTextureKernelIndex, resolution.x / 8, resolution.y / 8, 1);
		shaderBuffer?.Dispose();
	}

	private Vector2Int MapGraphCoordToTextureCoords(float x, float y)
	{
		Vector2Int pos = new Vector2Int() {
			x = (int)(x / generator.size.x * resolution.x),
			y = (int)(y / generator.size.y * resolution.y)
		};

		return pos;
	}

	private Vector2 MapTextureCoordToGraphCoords(int x, int y)
	{
		Vector2 pos = new Vector2() {
			x = (x / (float)resolution.x) * generator.size.x,
			y = (y / (float)resolution.y) * generator.size.y
		};

		return pos;
	}

	private void DrawSquare(int x, int y, int size, Color c)
	{
		Rect textureBounds = Rect.MinMaxRect(0, 0, resolution.x, resolution.y);

		for (int i = x - size; i < x + size; i++)
		{
			for (int j = y - size; j < y + size; j++)
			{
				if(textureBounds.Contains(new Vector2(i, j)))
				{
					texColors[i, j] = c;
				}
			}
		}
	}

	private void DrawWireSquare(int x, int y, int size, int thickness, Color c)
	{
		Rect textureBounds = Rect.MinMaxRect(0, 0, resolution.x, resolution.y);
		Rect innerRect = new Rect(x - size + thickness, y - size + thickness, size * 2 - thickness * 2, size * 2 - thickness * 2);

		for (int i = x - size; i < x + size; i++)
		{
			for (int j = y - size; j < y + size; j++)
			{
				Vector2 p = new Vector2(i, j);

				if (textureBounds.Contains(p) &&
					!innerRect.Contains(p))
				{
					texColors[i, j] = c;
				}
			}
		}
	}

	private void DrawCircle(int x, int y, int size, Color c)
	{
		for (int i = x - size; i < x + size; i++)
		{
			for (int j = y - size; j < y + size; j++)
			{
				if (i >= 0 &&
					i < resolution.x &&
					j >= 0 &&
					j < resolution.y &&
					Vector2Int.Distance(new Vector2Int(i, j), new Vector2Int(x, y)) <= size)
				{
					texColors[i, j] = c;
				}
			}
		}
	}

	private void DrawWireCircle(int x, int y, int size, int thickness, Color c)
	{
		Vector2Int p = new Vector2Int(x, y);

		for (int i = x - size; i < x + size; i++)
		{
			for (int j = y - size; j < y + size; j++)
			{
				if (i >= 0 &&
					i < resolution.x &&
					j >= 0 &&
					j < resolution.y &&
					Vector2Int.Distance(p, new Vector2Int(i, j)) <= size &&
					Vector2Int.Distance(p, new Vector2Int(i, j)) > size - thickness)
				{
					texColors[i, j] = c;
				}
			}
		}
	}

	private void DrawLine(int x0, int y0, int x1, int y1, int thickness, Color c)
	{
		Vector2 a = new Vector2(x0, y0);
		Vector2 b = new Vector2(x1, y1);

		float distance = Vector2.Distance(a, b);
		float steps = distance / (thickness / 2f);
		float stepSize = distance / steps;
		Vector2 direction = (b - a).normalized;

		for (float i = 0; i < steps; i++)
		{
			Vector2 pos = new Vector2() {
				x = x0 + direction.x * stepSize * i,
				y = y0 + direction.y * stepSize * i,
			};

			DrawCircle(Mathf.RoundToInt(pos.x), Mathf.RoundToInt(pos.y), thickness, c);
		}

		DrawCircle(x1, y1, thickness, c);
	}

	private CellCenter GetClosestCenterFromPoint(Vector2 point)
	{
		float smallestDistance = float.MaxValue;
		CellCenter c = null;

		foreach (var center in generator.delaunayCenters)
		{
			float d = Vector2.Distance(point, center.position);
			if (d < smallestDistance)
			{
				smallestDistance = d;
				c = center;
			}
		}

		return c;
	}

	private int[] GetClosestCenterForPixels()
	{

		//Send the data to the compute shader so the GPU can do the hard work
		CellData[] cellData = new CellData[generator.delaunayCenters.Count];
		ComputeBuffer cellDataBuffer = new ComputeBuffer(generator.delaunayCenters.Count, sizeof(int) * 2);
		ComputeBuffer cellIdByPixeldBuffer = new ComputeBuffer(resolution.x * resolution.y, sizeof(int)); //This is the buffer we're gonna read from

		for (int i = 0; i < cellData.Length; i++)
		{
			CellCenter c = generator.delaunayCenters[i];
			cellData[i] = new CellData() {
				position = MapGraphCoordToTextureCoords(c.position.x, c.position.y)
			};
		}

		cellDataBuffer.SetData(cellData);
		cellIdByPixeldBuffer.SetData(new int[resolution.x * resolution.y]); //we pass an empty array, since we just want to retrieve this data
		computeShader.SetBuffer(findClosestCellKernelIndex, "_CellData", cellDataBuffer);
		computeShader.SetBuffer(findClosestCellKernelIndex, "_CellIDByPixel", cellIdByPixeldBuffer);
		computeShader.SetInt("_Resolution", resolution.x);

		computeShader.Dispatch(findClosestCellKernelIndex, resolution.x / 8, resolution.y / 8, 1);

		//Get the result data back from the GPU
		int[] centersIDs = new int[resolution.x * resolution.y];
		cellIdByPixeldBuffer.GetData(centersIDs);

		cellDataBuffer?.Dispose();
		cellIdByPixeldBuffer?.Dispose();

		return centersIDs;
	}
	#endregion

	private void OnValidate()
	{
		if (generator == null)
			return;

		if (neighboorID < -1)
			neighboorID = -1;

		if (neighboorID >= generator.polygonCount)
			neighboorID = generator.polygonCount - 1;
	}
}
