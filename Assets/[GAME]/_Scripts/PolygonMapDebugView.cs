using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;

public class PolygonMapDebugView : MonoBehaviour
{
	public enum ViewModes
	{
		Map,
		Graph,
		Noise
	}

    public PolygonMap generator;
	public Image rend;
	public Vector2Int resolution = new Vector2Int(512, 512);
	[Header("Options")]
	public ViewModes mode;
	public bool showVoronoi;
	public bool showDelaunay;
	public int neighboorID = -1;
	public bool showBorder;
	public bool showWater;

	private Color[,] colors;

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
	}

	#region Generations
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
		ApplyChangesToTexture();
	}

	private void DrawMap()
	{
		
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

					Vector2Int pos0 = RemapPositionsToTexture(edge.d0.position.x, edge.d0.position.y);
					Vector2Int pos1 = RemapPositionsToTexture(edge.d1.position.x, edge.d1.position.y);
					DrawLine(pos0.x, pos0.y, pos1.x, pos1.y, 1, Color.black);
				}

				if (showVoronoi)
				{
					Vector2Int pos0 = RemapPositionsToTexture(edge.v0.position.x, edge.v0.position.y);
					Vector2Int pos1 = RemapPositionsToTexture(edge.v1.position.x, edge.v1.position.y);
					DrawLine(pos0.x, pos0.y, pos1.x, pos1.y, 1, Color.white);
				}
			}
		}

		//Delaunay triangulation points
		if (showDelaunay && generator.delaunayCenters != null)
		{
			foreach (var center in generator.delaunayCenters)
			{
				Vector2Int pos = RemapPositionsToTexture(center.position.x, center.position.y);
				DrawCircle(pos.x, pos.y, pointSize, Color.red);
			}
		}

		//Voronoi points
		if (showVoronoi && generator.voronoiCorners != null)
		{
			foreach (var corner in generator.voronoiCorners)
			{
				Vector2Int pos = RemapPositionsToTexture(corner.position.x, corner.position.y);
				DrawCircle(pos.x, pos.y, pointSize, Color.blue);
			}
		}

		//Neightbors visualization
		if (neighboorID >= 0 && generator.delaunayCenters.Count > 0)
		{
			CellCenter c = generator.delaunayCenters[neighboorID];

			if (showVoronoi || showDelaunay)
			{
				Vector2Int pos = RemapPositionsToTexture(c.position.x, c.position.y);
				DrawWireCircle(pos.x, pos.y, pointSize * 3, 2, Color.green);
			}

			if (showDelaunay)
			{
				for (int i = 0; i < c.neighborCells.Count; i++)
				{
					Vector2Int pos = RemapPositionsToTexture(c.neighborCells[i].position.x, c.neighborCells[i].position.y);
					DrawWireCircle(pos.x, pos.y, pointSize * 2, 2, Color.magenta);
				}
			}

			if (showVoronoi)
			{
				for (int i = 0; i < c.cellCorners.Count; i++)
				{
					Vector2Int pos = RemapPositionsToTexture(c.cellCorners[i].position.x, c.cellCorners[i].position.y);
					DrawWireCircle(pos.x, pos.y, pointSize * 2, 2, Color.yellow);
				}
			}

			for (int i = 0; i < c.borderEdges.Count; i++)
			{
				if (showDelaunay)
				{
					Vector2Int pos0 = RemapPositionsToTexture(c.borderEdges[i].d0.position.x, c.borderEdges[i].d0.position.y);
					Vector2Int pos1 = RemapPositionsToTexture(c.borderEdges[i].d1.position.x, c.borderEdges[i].d1.position.y);
					DrawLine(pos0.x, pos0.y, pos1.x, pos1.y, 1, new Color(0, .5f, .5f));
				}

				if (showVoronoi)
				{
					Vector2Int pos0 = RemapPositionsToTexture(c.borderEdges[i].v0.position.x, c.borderEdges[i].v0.position.y);
					Vector2Int pos1 = RemapPositionsToTexture(c.borderEdges[i].v1.position.x, c.borderEdges[i].v1.position.y);
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
					Vector2Int pos = RemapPositionsToTexture(corner.position.x, corner.position.y);
					DrawSquare(pos.x, pos.y, pointSize * 2, Color.red);
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
					Vector2Int pos = RemapPositionsToTexture(corner.position.x, corner.position.y);
					DrawSquare(pos.x, pos.y, pointSize * 2, Color.blue);
				}
			}

			foreach (var cell in generator.delaunayCenters)
			{
				if (cell.isWater)
				{
					Vector2Int pos = RemapPositionsToTexture(cell.position.x, cell.position.y);
					DrawSquare(pos.x, pos.y, pointSize * 2, Color.blue);
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
				float value = Mathf.PerlinNoise(x * generator.noiseSize + generator.noiseSeed, y * generator.noiseSize + generator.noiseSeed);
				colors[x, y] = new Color(value, value, value, 1);
			}
		}
	}
	#endregion

	#region Helper Functions
	private Vector2Int RemapPositionsToTexture(float x, float y)
	{
		Vector2Int pos = new Vector2Int() {
			x = (int)(x / generator.size.x * resolution.x),
			y = (int)(y / generator.size.y * resolution.y)
		};

		return pos;
	}

	private void ApplyChangesToTexture()
	{
		Texture2D tex = new Texture2D(resolution.x, resolution.y);
		tex.filterMode = FilterMode.Point;

		Color[] finalColors = new Color[resolution.x * resolution.y];

		for (int i = 0; i < resolution.x; i++)
		{
			for (int j = 0; j < resolution.y; j++)
			{
				finalColors[i + j * resolution.y] = colors[i, j];
			}
		}

		tex.SetPixels(finalColors);
		tex.Apply();
		rend.sprite = Sprite.Create(tex, new Rect(0, 0, resolution.x, resolution.y), Vector2.one * 0.5f, 100, 0, SpriteMeshType.FullRect);
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

	private bool PolygonContainsPoint(Vector2[] polyPoints, Vector2 p)
	{
		var j = polyPoints.Length - 1;
		var inside = false;
		for (int i = 0; i < polyPoints.Length; j = i++)
		{
			var pi = polyPoints[i];
			var pj = polyPoints[j];
			if (((pi.y <= p.y && p.y < pj.y) || (pj.y <= p.y && p.y < pi.y)) &&
				(p.x < (pj.x - pi.x) * (p.y - pi.y) / (pj.y - pi.y) + pi.x))
				inside = !inside;
		}
		return inside;
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

		if(Application.isPlaying)
			GenerateDebugTexture();
	}
}
