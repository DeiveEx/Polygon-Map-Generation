using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using NaughtyAttributes;

public class PolygonMapDebugView : MonoBehaviour
{
	public enum ViewModes
	{
		Map,
		Graph,
		Noise
	}

    public PolygonMap generator;
	public RawImage rend;
	public Vector2Int resolution = new Vector2Int(512, 512);
	public ComputeShader computeShader;
	[Header("Options")]
	public ViewModes mode;
	public bool showVoronoi;
	public bool showDelaunay;
	public int neighboorID = -1;
	public bool showBorder;
	public bool showWater;

	private Color[,] colors;
	private RenderTexture rt;
	private ComputeBuffer shaderBuffer;
	private int buildTextureKernelIndex;
	private ColorData[] colorData;

	private struct ColorData
	{
		public Vector2Int position;
		public Color color;
	}

	private void Awake()
	{
		//Create a render texture and enable random write so we can rend things to it
		rt = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.ARGB32);
		rt.enableRandomWrite = true;
		rt.Create();

		//Set the texture for the renderer
		rend.texture = rt;

		//Set the texture for the shader
		buildTextureKernelIndex = computeShader.FindKernel("CSMain");

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

		shaderBuffer?.Dispose();
		rt?.Release();
	}

	#region Generations
	[Button]
	private void GenerateDebugTexture()
	{
		if (rend == null || generator == null)
			return;

		colors = new Color[resolution.x, resolution.y];

		//Draw the debug info into the texture
		switch (mode)
		{
			case ViewModes.Map:
				DrawMap();
				break;
			case ViewModes.Graph:
				DrawGraphs();
				break;
			case ViewModes.Noise:
				DrawNoise();
				break;
			default:
				break;
		}

		//Create the texture and assign the texture
		//ApplyChangesToTexture();
	}

	private void DrawMap()
	{
		if (showVoronoi)
		{
			//Generate random colors for each cell
			Color[] cellColors = new Color[generator.delaunayCenters.Count];

			for (int i = 0; i < cellColors.Length; i++)
			{
				cellColors[i] = Random.ColorHSV();
				cellColors[i].a = 1;
			}

			//Paint the cells
			for (int x = 0; x < resolution.x; x++)
			{
				for (int y = 0; y < resolution.y; y++)
				{
					Vector2 pos = MapTextureCoordToGraphCoords(x, y);
					CellCenter c = GetClosestCenterFromPoint(pos);

					if (c != null)
					{
						colors[x, y] = cellColors[c.index];
					}
				}
			}

			return;
		}

		if (showWater)
		{
			Color water = Color.blue;
			Color land = new Color(.8f, .8f, .5f);

			//Paint the cells
			for (int x = 0; x < resolution.x; x++)
			{
				for (int y = 0; y < resolution.y; y++)
				{
					Vector2 pos = MapTextureCoordToGraphCoords(x, y);
					CellCenter c = GetClosestCenterFromPoint(pos);

					if (c != null)
					{
						colors[x, y] = c.isWater ? water : land;
					}
				}
			}

			return;
		}
	}

	private void DrawGraphs()
	{
		int pointSize = 5;

		//Edges
		if (generator.mapEdges != null)
		{
			foreach (var edge in generator.mapEdges)
			{
				if (showDelaunay)
				{

					Vector2Int pos0 = MapGraphCoordToTextureCoords(edge.d0.position.x, edge.d0.position.y);
					Vector2Int pos1 = MapGraphCoordToTextureCoords(edge.d1.position.x, edge.d1.position.y);
					DrawLine(pos0.x, pos0.y, pos1.x, pos1.y, 1, Color.black);
				}

				if (showVoronoi)
				{
					Vector2Int pos0 = MapGraphCoordToTextureCoords(edge.v0.position.x, edge.v0.position.y);
					Vector2Int pos1 = MapGraphCoordToTextureCoords(edge.v1.position.x, edge.v1.position.y);
					DrawLine(pos0.x, pos0.y, pos1.x, pos1.y, 1, Color.white);
				}
			}
		}

		//Delaunay triangulation points
		if (showDelaunay && generator.delaunayCenters != null)
		{
			foreach (var center in generator.delaunayCenters)
			{
				Vector2Int pos = MapGraphCoordToTextureCoords(center.position.x, center.position.y);
				DrawCircle(pos.x, pos.y, pointSize, Color.red);
			}
		}

		//Voronoi points
		if (showVoronoi && generator.voronoiCorners != null)
		{
			foreach (var corner in generator.voronoiCorners)
			{
				Vector2Int pos = MapGraphCoordToTextureCoords(corner.position.x, corner.position.y);
				DrawCircle(pos.x, pos.y, pointSize, Color.blue);
			}
		}

		//Neightbors visualization
		if (neighboorID >= 0 && generator.delaunayCenters.Count > 0)
		{
			CellCenter c = generator.delaunayCenters[neighboorID];

			if (showVoronoi || showDelaunay)
			{
				Vector2Int pos = MapGraphCoordToTextureCoords(c.position.x, c.position.y);
				DrawWireCircle(pos.x, pos.y, pointSize * 3, 2, Color.green);
			}

			if (showDelaunay)
			{
				for (int i = 0; i < c.neighborCells.Count; i++)
				{
					Vector2Int pos = MapGraphCoordToTextureCoords(c.neighborCells[i].position.x, c.neighborCells[i].position.y);
					DrawWireCircle(pos.x, pos.y, pointSize * 2, 2, Color.magenta);
				}
			}

			if (showVoronoi)
			{
				for (int i = 0; i < c.cellCorners.Count; i++)
				{
					Vector2Int pos = MapGraphCoordToTextureCoords(c.cellCorners[i].position.x, c.cellCorners[i].position.y);
					DrawWireCircle(pos.x, pos.y, pointSize * 2, 2, Color.yellow);
				}
			}

			for (int i = 0; i < c.borderEdges.Count; i++)
			{
				if (showDelaunay)
				{
					Vector2Int pos0 = MapGraphCoordToTextureCoords(c.borderEdges[i].d0.position.x, c.borderEdges[i].d0.position.y);
					Vector2Int pos1 = MapGraphCoordToTextureCoords(c.borderEdges[i].d1.position.x, c.borderEdges[i].d1.position.y);
					DrawLine(pos0.x, pos0.y, pos1.x, pos1.y, 1, new Color(0, .5f, .5f));
				}

				if (showVoronoi)
				{
					Vector2Int pos0 = MapGraphCoordToTextureCoords(c.borderEdges[i].v0.position.x, c.borderEdges[i].v0.position.y);
					Vector2Int pos1 = MapGraphCoordToTextureCoords(c.borderEdges[i].v1.position.x, c.borderEdges[i].v1.position.y);
					DrawLine(pos0.x, pos0.y, pos1.x, pos1.y, 1, Color.cyan);
				}
			}
		}

		//Borders
		if (showBorder && generator.voronoiCorners != null)
		{
			foreach (var corner in generator.voronoiCorners)
			{
				if (corner.isBorder)
				{
					Vector2Int pos = MapGraphCoordToTextureCoords(corner.position.x, corner.position.y);
					DrawSquare(pos.x, pos.y, pointSize * 2, Color.red);
				}
			}

			foreach (var center in generator.delaunayCenters)
			{
				if (center.isBorder)
				{
					Vector2Int pos = MapGraphCoordToTextureCoords(center.position.x, center.position.y);
					DrawWireSquare(pos.x, pos.y, pointSize * 2, 2, Color.red);
				}
			}
		}

		//Water
		if (showWater && generator.voronoiCorners != null && generator.delaunayCenters != null)
		{
			foreach (var corner in generator.voronoiCorners)
			{
				if (corner.isWater)
				{
					Vector2Int pos = MapGraphCoordToTextureCoords(corner.position.x, corner.position.y);
					DrawSquare(pos.x, pos.y, pointSize * 2, Color.blue);
				}
			}

			foreach (var cell in generator.delaunayCenters)
			{
				if (cell.isWater)
				{
					Vector2Int pos = MapGraphCoordToTextureCoords(cell.position.x, cell.position.y);
					DrawWireCircle(pos.x, pos.y, pointSize * 2, 2, Color.blue);
				}
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

				colors[x, y] = value > value2 ? Color.white : Color.black;
			}
		}
	}
	#endregion

	#region Helper Functions

	private void ApplyChangesToTexture()
	{
		//Create a new Buffer
		shaderBuffer?.Dispose();
		shaderBuffer = new ComputeBuffer(resolution.x * resolution.y, sizeof(float) * 4 + sizeof(int) * 2); //4 because a color has 4 channels, 2 because the position has X and Y
		colorData = new ColorData[resolution.x * resolution.y];

		for (int x = 0; x < resolution.x; x++)
		{
			for (int y = 0; y < resolution.y; y++)
			{
				ColorData data = new ColorData() {
					position = new Vector2Int(x, y),
					color = colors[x, y]
				};

				colorData[x + y * resolution.y] = data;
			}
		}

		shaderBuffer.SetData(colorData);
		computeShader.SetBuffer(buildTextureKernelIndex, "_ColorData", shaderBuffer);
		computeShader.SetInt("_ColorsAmount", resolution.x * resolution.y);
		computeShader.SetInt("_Resolution", resolution.x );
		computeShader.Dispatch(buildTextureKernelIndex, resolution.x / 8, resolution.y / 8, 1);

		//Texture2D tex = new Texture2D(resolution.x, resolution.y);
		//tex.filterMode = FilterMode.Point;

		//Color[] finalColors = new Color[resolution.x * resolution.y];

		//for (int i = 0; i < resolution.x; i++)
		//{
		//	for (int j = 0; j < resolution.y; j++)
		//	{
		//		finalColors[i + j * resolution.y] = colors[i, j];
		//	}
		//}

		//tex.SetPixels(finalColors);
		//tex.Apply();
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
					colors[i, j] = c;
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
					colors[i, j] = c;
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
					colors[i, j] = c;
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
					colors[i, j] = c;
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
