using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;
using csDelaunay; //The Voronoi Library
using System.Linq;

public class PolygonMap : MonoBehaviour
{
	//Properties
	public bool useCustomSeed;
	public int seed;
	[HideInInspector]
	public int noiseSeed; //Ther perlin noise seed
	public int variation;
	public int polygonCount = 200; //The number of polygons/sites we want
	public Vector2 size;
    public int relaxation = 0;
    public float noiseSize = 1;
	public float borderSize = 1f;

	//Graphs. Here are the info necessary to build both the Voronoi Graph than the Delaunay triangulation Graph
	public List<CellCenter> delaunayCenters = new List<CellCenter>();
	public List<CellCorner> voronoiCorners = new List<CellCorner>();
	public List<MapEdge> mapEdges = new List<MapEdge>();

	//Events
	public event System.Action onMapGenerated;

	//Constants
	private const float LAKE_THRESHOLD = 0.3f; //0 to 1. Percentage of how many corners must be water for a cell center to be water too

	private void Start()
	{
		Generate();
	}

	#region Generation
	[Button]
	public void Generate()
	{
		if (!Application.isPlaying)
		{
			Debug.LogWarning("Only Available in Play Mode");
			return;
		}

		//Clear all map info
		ResetMapInfo();

		if (!useCustomSeed)
		{
			seed = Random.Range(int.MinValue, int.MaxValue);
		}

		//Set the seed for the random system
		Random.InitState(seed);
		noiseSeed = seed / 100000; //We use a second, lower seed for the perlin noise because numbers too big can cause problems

		List<Vector2> points = GenerateRandomPoints();
		GenerateGraphs(points);
		AssignWater();
		AssignOceanCoastAndLand();
		//AssignElevation();

		//Execute an event saying we finished our generation
		onMapGenerated?.Invoke();
	}

	private void ResetMapInfo()
	{
		voronoiCorners.Clear();
		delaunayCenters.Clear();
		mapEdges.Clear();
	}

	private List<Vector2> GenerateRandomPoints()
	{
		//Generate random points
		List<Vector2> points = new List<Vector2>();

		for (int i = 0; i < polygonCount; i++)
		{
			Vector2 p = new Vector2() {
				x = Random.Range(0, size.x),
				y = Random.Range(0, size.y)
			};

			points.Add(p);
		}

		return points;
	}

	private void GenerateGraphs(List<Vector2> points)
	{
		//Generate the Voronoi
		Rectf bounds = new Rectf(0, 0, size.x, size.y);
		Voronoi voronoi = new Voronoi(points, bounds, relaxation);

		//Cell centers
		Dictionary<Vector2, CellCenter> centersLookup = new Dictionary<Vector2, CellCenter>(); //Quick way to access a CellCenter through its position
		foreach (var site in voronoi.SitesIndexedByLocation)
		{
			CellCenter c = new CellCenter();
			c.index = delaunayCenters.Count;
			c.position = site.Key;

			delaunayCenters.Add(c);
			centersLookup.Add(c.position, c);
		}

		//Cell Corners
		Dictionary<Vector2, CellCorner> cornersLookup = new Dictionary<Vector2, CellCorner>(); //Quick way to access a Cell Corner thorugh its position
		foreach (var edge in voronoi.Edges)
		{
			//If the edge doesn't have clipped ends, it was not withing bounds
			if (edge.ClippedEnds == null)
				continue;

			if (!cornersLookup.ContainsKey(edge.ClippedEnds[LR.LEFT]))
			{
				CellCorner c1 = new CellCorner();
				c1.position = edge.ClippedEnds[LR.LEFT];

				cornersLookup.Add(c1.position, c1);
			}

			if (!cornersLookup.ContainsKey(edge.ClippedEnds[LR.RIGHT]))
			{
				CellCorner c2 = new CellCorner();
				c2.position = edge.ClippedEnds[LR.RIGHT];

				cornersLookup.Add(c2.position, c2);
			}
		}

		foreach (var corner in cornersLookup)
		{
			CellCorner c = corner.Value;
			c.index = voronoiCorners.Count;
			c.position = corner.Key;
			c.isBorder = c.position.x == 0 || c.position.x == size.x || c.position.y == 0 || c.position.y == size.y;

			voronoiCorners.Add(c);
		}

		//Define a local helper function to help with the loop below
		void AddPointToPointList<T>(List<T> list, T point) where T : MapPoint
		{
			if (!list.Contains(point))
				list.Add(point);
		}

		//Voronoi and Delaunay edges. Each edge point to two cells and two corners, so we can store both the sites and corners into a single edge object (thus making two edges into one object)
		foreach (var voronoiEdge in voronoi.Edges)
		{
			if (voronoiEdge.ClippedEnds == null)
				continue;

			MapEdge edge = new MapEdge();
			edge.index = mapEdges.Count;

			//Set the voronoi edge
			edge.v0 = cornersLookup[voronoiEdge.ClippedEnds[LR.LEFT]];
			edge.v1 = cornersLookup[voronoiEdge.ClippedEnds[LR.RIGHT]];

			//Set the Delaunay edge
			edge.d0 = centersLookup[voronoiEdge.LeftSite.Coord];
			edge.d1 = centersLookup[voronoiEdge.RightSite.Coord];

			mapEdges.Add(edge);

			/*Set the relationships*/

			//Set the relationship between this edge and the connected cells centers/corners
			edge.d0.borderEdges.Add(edge);
			edge.d1.borderEdges.Add(edge);
			edge.v0.connectedEdges.Add(edge);
			edge.v1.connectedEdges.Add(edge);

			//Set the relationship between the CELL CENTERS connected to this edge
			AddPointToPointList(edge.d0.neighborCells, edge.d1);
			AddPointToPointList(edge.d1.neighborCells, edge.d0);

			//Set the relationship between the CORNERS connected to this edge
			AddPointToPointList(edge.v0.neighboorCorners, edge.v1);
			AddPointToPointList(edge.v1.neighboorCorners, edge.v0);

			//Set the relationship of the CORNERS connected to this edge and the CELL CENTERS connected to this edge
			AddPointToPointList(edge.d0.cellCorners, edge.v0);
			AddPointToPointList(edge.d0.cellCorners, edge.v1);

			AddPointToPointList(edge.d1.cellCorners, edge.v0);
			AddPointToPointList(edge.d1.cellCorners, edge.v1);

			//Same as above, but the other way around
			AddPointToPointList(edge.v0.touchingCells, edge.d0);
			AddPointToPointList(edge.v0.touchingCells, edge.d1);

			AddPointToPointList(edge.v1.touchingCells, edge.d0);
			AddPointToPointList(edge.v1.touchingCells, edge.d1);
		}
	}

	private void AssignWater()
	{
		//Define a local helper function to determine if a corner is a land or not
		//We use a perlin noise to determine the shape of the island, but anything can be used. We also use a radius from the center of the map, so the island is always surrounded by water
		bool IsLand(Vector2 position)
		{
			//Normalize the position to a -1 to 1 value
			Vector2 normalizedPosition = new Vector2() {
				x = ((position.x / size.x) - 0.5f) * 2,
				y = ((position.y / size.y) - 0.5f) * 2
			};

			float value = Mathf.PerlinNoise(position.x * noiseSize + noiseSeed, position.y * noiseSize + noiseSeed); //Unity's perlin noise function isn't random, so we need to add a "seed" to offset the values

			//We check if the value of the perlin is greater than the border value
			return value > Mathf.Pow(normalizedPosition.magnitude, borderSize);
		}

		//Define if a corner is land or not based on some shape function (defined above)
		foreach (var corner in voronoiCorners)
		{
			corner.isWater = !IsLand(corner.position);
		}
	}

	private void AssignOceanCoastAndLand()
	{
		Queue<CellCenter> queue = new Queue<CellCenter>();
		int numWater = 0;

		//Set the cell to water / ocean / border
		foreach (var center in delaunayCenters)
		{
			numWater = 0;

			foreach (var corner in center.cellCorners)
			{
				if (corner.isBorder)
				{
					center.isBorder = true;
					center.isOcean = true;
					corner.isWater = true;
					queue.Enqueue(center);
				}

				if (corner.isWater)
				{
					numWater += 1;
				}
			}

			center.isWater = center.isOcean || numWater >= center.cellCorners.Count * LAKE_THRESHOLD;
		}

		//Every cell around a border must be a ocean too
		while (queue.Count > 0)
		{
			CellCenter c = queue.Dequeue();

			foreach (var n in c.neighborCells)
			{
				if (n.isWater && !n.isOcean)
				{
					n.isOcean = true;
					queue.Enqueue(n);
				}
			}
		}

		////Set the Coast cells based on the neighboors. If a cell has at least one ocean and one land neighboor, then the cell is a coast
		//foreach (var cell in delaunayCenters)
		//{
		//	int numOcean = 0;
		//	int numLand = 0;

		//	foreach (var n in cell.neighborCells)
		//	{
		//		numOcean += n.isOcean ? 1 : 0;
		//		numLand += !n.isWater ? 1 : 0;
		//	}

		//	cell.isCoast = numOcean > 0 && numLand > 0;
		//}

		////Set the corners attributes based on the connected cells. If all connected cells are ocean, then the corner is ocean. If all cells are land, then the corner is land. Otherwise the corner is a coast
		//foreach (var corner in voronoiCorners)
		//{
		//	int numOcean = 0;
		//	int numLand = 0;

		//	foreach (var cell in corner.touchingCells)
		//	{
		//		numOcean += cell.isOcean ? 1 : 0;
		//		numLand += !cell.isWater ? 1 : 0;
		//	}

		//	corner.isOcean = numOcean == corner.touchingCells.Count;
		//	corner.isCoast = numOcean > 0 && numLand > 0;
		//	corner.isWater = corner.isBorder || numLand != corner.touchingCells.Count && !corner.isCoast;
		//}
	}

	private void AssignElevation()
	{
		//TODO uncomment this when doind elevations
		//Queue<CellCorner> queue = new Queue<CellCorner>();
		//foreach (var corner in voronoiCorners)
		//{
		//	if (corner.isBorder)
		//	{
		//		corner.elevation = 0;
		//		queue.Enqueue(corner);
		//	}
		//	else
		//	{
		//		corner.elevation = Mathf.Infinity;
		//	}
		//}
	}
	#endregion
}
