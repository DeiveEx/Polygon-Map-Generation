using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
using TMPro;
using System.Linq;

public class PolygonMapDebugView : MonoBehaviour
{
	public enum ViewBG
	{
		VoronoiCells,
		Noise,
		WaterAndLand,
		Elevation,
	}

	[Flags] //This attribute turns the enum into a bitmask, and Unity has a special inspactor for bitmasks. We use a bitmask so we can draw more modes at once. Since this is a int, we can have up to 32 values
	public enum Overlays
	{
		VoronoiEdges		= 1 << 0, //Bit shifts the bit value of "1" (something like 0001, but with 32 digits, since this is a int) 0 bit to the left
		VoronoiCorners		= 1 << 1, //Same as above, but shifting 1 bit to the left, so the result will be "0010" (which is 2 in Decimal)
		DelaunayEdges		= 1 << 2,
		DelaunayCorners		= 1 << 3,
		Selected			= 1 << 4,
		Borders				= 1 << 5,
		Coast				= 1 << 6,
		Slopes				= 1 << 7,
	}

    public PolygonMap generator;
	public ComputeShader computeShader;
	public RawImage rend;
	public TMP_Text infoText;
	[Header("Options")]
	public Vector2Int resolution = new Vector2Int(512, 512);
	public ViewBG background;
	public Overlays overlays;
	public int selectedID = -1;

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
		rt.filterMode = FilterMode.Point;
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

	private void Update()
	{
		if ((overlays & Overlays.Selected) != 0 && Input.GetMouseButtonDown(0) && RectTransformUtility.RectangleContainsScreenPoint(rend.rectTransform, Input.mousePosition))
		{
			Vector2 transformedPos = (Vector2)Input.mousePosition - rend.rectTransform.anchoredPosition;
			transformedPos = transformedPos / rend.rectTransform.rect.size;
			transformedPos = transformedPos * resolution;

			int[] cellIDs = GetClosestCenterForPixels();

			selectedID = cellIDs[(int)transformedPos.x + (int)transformedPos.y * resolution.y];

			GenerateDebugTexture();
		}
	}

	#region Generation
	private void GenerateDebugTexture()
	{
		if (!Application.isPlaying)
		{
			Debug.LogWarning("Only Available in Play Mode");
			return;
		}

		if (rend == null || generator == null || generator.cells == null || generator.corners.Count == 0)
			return;

		texColors = new Color[resolution.x, resolution.y];

		//Draw the debug info into the texture. The order here defines the draw order

		//BACKGROUND
		switch (background)
		{
			case ViewBG.VoronoiCells:
				DrawVoronoiCells();
				break;
			case ViewBG.Noise:
				DrawNoise();
				break;
			case ViewBG.WaterAndLand:
				DrawWater();
				break;
			case ViewBG.Elevation:
				DrawElevation();
				break;
			default:
				break;
		}

		//OVERLAYS
		if ((overlays & Overlays.Borders) != 0)
			DrawMapBorders();

		if ((overlays & Overlays.VoronoiEdges) != 0) //Here we "create" a new byte value by comparing "mode" with "voronoiCells" using a "&" (AND) bitwise operator. As an example, the comparision works like this: 0011 & 0110 = 0010. Then we compare with "0", since zero is "0000".
			DrawVoronoiEdges();

		if ((overlays & Overlays.VoronoiCorners) != 0)
			DrawVoronoiCorners();

		if ((overlays & Overlays.DelaunayEdges) != 0)
			DrawDelaunayEdges();

		if ((overlays & Overlays.DelaunayCorners) != 0)
			DrawDelaunayCorners();

		if ((overlays & Overlays.Coast) != 0)
			DrawCoast();

		if ((overlays & Overlays.Slopes) != 0)
			DrawSlopes();

		//We want these to be over everything
		if ((overlays & Overlays.Selected) != 0)
		{
			DrawNeighboors();
			infoText.text = GetInfoFromCell();
		}
		else
		{
			infoText.text = string.Empty;
		}

		//Create the texture and assign the texture using a Helper Compute Shader
		ApplyChangesToTexture();
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

	private string GetInfoFromCell()
	{
		CellCenter c = generator.cells.First(x => x.index == selectedID);

		string info = $"CELL:\n";

		info += "\n- " + string.Join("\n- ",
			$"id: {c.index}",
			$"elevation: {c.elevation}"
		);

		info += "\n\nEDGES:\n";

		foreach (var edge in c.borderEdges)
		{
			info += "\n- " + string.Join("\n",
				$"id: {edge.index}",
				$"V: {edge.v0.index}; {edge.v1.index}\tD: {edge.d0.index}; {edge.d1.index}"
			);
		}

		info += "\n\nCORNERS:\n";

		foreach (var corner in c.cellCorners)
		{
			info += "\n- " + string.Join("\n",
				$"id: {corner.index}",
				$"elevation: {corner.elevation}"
			);
		}

		return info;
	}

	private CellCenter GetClosestCenterFromPoint(Vector2 point)
	{
		float smallestDistance = float.MaxValue;
		CellCenter c = null;

		foreach (var center in generator.cells)
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
		CellData[] cellData = new CellData[generator.cells.Count];
		ComputeBuffer cellDataBuffer = new ComputeBuffer(generator.cells.Count, sizeof(int) * 2);
		ComputeBuffer cellIdByPixeldBuffer = new ComputeBuffer(resolution.x * resolution.y, sizeof(int)); //This is the buffer we're gonna read from

		for (int i = 0; i < cellData.Length; i++)
		{
			CellCenter c = generator.cells[i];
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

	#region Draw Views
	private void DrawVoronoiCells()
	{
		int[] cellIDs = GetClosestCenterForPixels();

		for (int x = 0; x < resolution.x; x++)
		{
			for (int y = 0; y < resolution.y; y++)
			{
				int currentCellID = cellIDs[x + y * resolution.y];
				float value = currentCellID / (float)generator.cells.Count;
				texColors[x, y] = new Color(value, value, value);
			}
		}
	}

	private void DrawVoronoiEdges()
	{
		foreach (var edge in generator.edges)
		{
			Vector2Int pos0 = MapGraphCoordToTextureCoords(edge.v0.position.x, edge.v0.position.y);
			Vector2Int pos1 = MapGraphCoordToTextureCoords(edge.v1.position.x, edge.v1.position.y);
			DrawLine(pos0.x, pos0.y, pos1.x, pos1.y, 1, Color.white);
		}
	}

	private void DrawVoronoiCorners()
	{
		foreach (var corner in generator.corners)
		{
			Vector2Int pos = MapGraphCoordToTextureCoords(corner.position.x, corner.position.y);
			DrawCircle(pos.x, pos.y, POINT_SIZE, Color.blue);
		}
	}

	private void DrawDelaunayEdges()
	{
		foreach (var edge in generator.edges)
		{
			Vector2Int pos0 = MapGraphCoordToTextureCoords(edge.d0.position.x, edge.d0.position.y);
			Vector2Int pos1 = MapGraphCoordToTextureCoords(edge.d1.position.x, edge.d1.position.y);
			DrawLine(pos0.x, pos0.y, pos1.x, pos1.y, 1, Color.black);
		}
	}

	private void DrawDelaunayCorners()
	{
		foreach (var center in generator.cells)
		{
			Vector2Int pos = MapGraphCoordToTextureCoords(center.position.x, center.position.y);
			DrawCircle(pos.x, pos.y, POINT_SIZE, Color.red);
		}
	}

	private void DrawNeighboors()
	{
		CellCenter c = generator.cells[selectedID];

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
		int[] cellIDs = GetClosestCenterForPixels();

		for (int x = 0; x < resolution.x; x++)
		{
			for (int y = 0; y < resolution.y; y++)
			{
				CellCenter c = generator.cells[cellIDs[x + y * resolution.y]];

				if (c.isBorder)
				{
					texColors[x, y] = Color.red;
				}
			}
		}

		Color borderCorner = new Color(1, .5f, .5f);

		foreach (var corner in generator.corners)
		{
			if (corner.isBorder)
			{
				Vector2Int pos = MapGraphCoordToTextureCoords(corner.position.x, corner.position.y);
				DrawCircle(pos.x, pos.y, POINT_SIZE * 2, borderCorner);
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
				float value2 = 0.3f + 0.3f * normalizedPos.magnitude * normalizedPos.magnitude; //Same formula used in the generation

				value = BetterPerlinNoise.SamplePoint(perlinPos.x * generator.noiseSize + generator.noiseSeed, perlinPos.y * generator.noiseSize + generator.noiseSeed, generator.octaves, generator.persistence, generator.lacunarity);

				texColors[x, y] = value > value2 ? Color.gray : Color.black;
				texColors[x, y] = Color.white * value;
				texColors[x, y].a = 1;
			}
		}
	}

	private void DrawWater()
	{
		Color ocean = new Color(.1f,.1f, .5f);
		Color water = new Color(.5f, .5f, .7f);
		Color land = new Color(.7f, .7f, .5f);
		Color coast = new Color(.5f, .5f, .3f);

		int[] cellIDs = GetClosestCenterForPixels();

		for (int x = 0; x < resolution.x; x++)
		{
			for (int y = 0; y < resolution.y; y++)
			{
				int currentCellID = cellIDs[x + y * resolution.y];
				CellCenter c = generator.cells[currentCellID];

				if (c.isWater)
				{
					if (c.isOcean)
					{
						texColors[x, y] = ocean;
					}
					else
					{
						texColors[x, y] = water;
					}
				}
				else
				{
					if (c.isCoast)
					{
						texColors[x, y] = coast;
					}
					else
					{
						texColors[x, y] = land;
					}
				}
			}
		}
	}

	private void DrawCoast()
	{
		foreach (var corner in generator.corners)
		{
			if (corner.isCoast)
			{
				Vector2Int pos = MapGraphCoordToTextureCoords(corner.position.x, corner.position.y);
				DrawWireCircle(pos.x, pos.y, POINT_SIZE, 2, Color.white);
			}
		}
	}

	private void DrawElevation()
	{
		int[] cellIDs = GetClosestCenterForPixels();
		Color low = Color.black;
		Color high = Color.white;
		Color water = Color.blue * 0.5f;
		water.a = 1;

		for (int x = 0; x < resolution.x; x++)
		{
			for (int y = 0; y < resolution.y; y++)
			{
				CellCenter c = generator.cells[cellIDs[x + y * resolution.y]];

				if (c.elevation < 0)
				{
					texColors[x, y] = water;
				}
				else
				{
					texColors[x, y] = Color.Lerp(low, high, c.elevation);
				}
			}
		}
	}

	private void DrawSlopes()
	{
		foreach (var corner in generator.corners)
		{
			if (corner.isOcean || corner.isCoast)
			{
				continue;
			}

			if (corner.downslope == null)
			{
				continue;
			}

			Vector2Int pos = MapGraphCoordToTextureCoords(corner.position.x, corner.position.y);
			Vector2Int pos2 = MapGraphCoordToTextureCoords(corner.downslope.position.x, corner.downslope.position.y);
			Vector2 dir = pos2 - pos;
			DrawArrow(pos.x, pos.y, dir, dir.magnitude * 0.4f, POINT_SIZE / 3, Color.red);
		}
	}

	#endregion

	#region Draw Shapes
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

	private void DrawArrow(int x0, int y0, int x1, int y1, int thickness, Color c)
	{
		Vector2 dir = new Vector2(x0, y0) - new Vector2(x1, y1);
		float headSize = dir.magnitude * .3f;
		float arrowHeadAngle = 45;

		DrawLine(x0, y0, x1, y1, thickness, c);

		Vector2 arrowSide1 = Quaternion.AngleAxis(arrowHeadAngle, Vector3.forward) * dir;
		Vector2 arrowSide2 = Quaternion.AngleAxis(-arrowHeadAngle, Vector3.forward) * dir;

		DrawLine(x1, y1, (int)(x1 + arrowSide1.normalized.x * headSize), (int)(y1 + arrowSide1.normalized.y * headSize), thickness, c);
		DrawLine(x1, y1, (int)(x1 + arrowSide2.normalized.x * headSize), (int)(y1 + arrowSide2.normalized.y * headSize), thickness, c);
	}

	private void DrawArrow(int x, int y, Vector2 dir, float lenght, int thickness, Color c)
	{
		DrawArrow(x, y, (int)(x + dir.normalized.x * lenght), (int)(y + dir.normalized.y * lenght), thickness, c);
	}
	#endregion

	private void OnValidate()
	{
		if (generator == null)
			return;

		if (selectedID < 0)
			selectedID = 0;

		if (selectedID >= generator.polygonCount)
			selectedID = generator.polygonCount - 1;

		if (Application.isPlaying)
			GenerateDebugTexture();
	}
}
