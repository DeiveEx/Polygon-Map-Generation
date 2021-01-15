using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NaughtyAttributes;
using csDelaunay; //The Voronoi Library
using System.Linq;

public class PolygonMap : MonoBehaviour
{
	//Properties
	public bool useCustomSeeds;

	[Header("Map")]
	public int seed;
	public int polygonCount = 200; //The number of polygons/sites we want
	public Vector2 size;
    public int relaxation = 0;
	public AnimationCurve elevationCurve = AnimationCurve.Linear(0, 0, 1, 1);

	[Header("Noise")]
	public int noiseSeed;
    public float noiseSize = 1;
    [Range(1, 4)] public int octaves = 4;

	[Header("Rivers")]
	public int springsSeed;
	public int numberOfSprings = 5;

	//Graphs. Here are the info necessary to build both the Voronoi Graph than the Delaunay triangulation Graph
	public List<CellCenter> cells = new List<CellCenter>(); //The center of each cell makes up a corner for the Delaunay triangles
	public List<CellCorner> corners = new List<CellCorner>(); //The corners of the cells. Also the center of the Delaunay triangles
	public List<CellEdge> edges = new List<CellEdge>(); //We use a single object here, but we are representing two edges for each object (the voronoi edge and the Delaunay Edge)

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

		if (!useCustomSeeds)
		{
			seed = Random.Range(int.MinValue, int.MaxValue);
		}

		//Set the seed for the random system
		Random.InitState(seed);

		List<Vector2> points = GenerateRandomPoints();
		GenerateGraphs(points);
		AssignWater(); //He is where we define the general shape of the island
		AssignOceanCoastAndLand();
		AssignElevations(); //For this case, we are making that the farthest from the coast, the higher the elevation
		AddRivers();

		//Execute an event saying we finished our generation
		onMapGenerated?.Invoke();
	}

	private void ResetMapInfo()
	{
		corners.Clear();
		cells.Clear();
		edges.Clear();
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
		foreach (var site in voronoi.SitesIndexedByLocation)
		{
			CellCenter c = new CellCenter();
			c.index = cells.Count;
			c.position = site.Key;

			cells.Add(c);
		}

		//Cell Corners
		foreach (var edge in voronoi.Edges)
		{
			//If the edge doesn't have clipped ends, it was not withing bounds
			if (edge.ClippedEnds == null)
				continue;
			
			if(!corners.Any(x => x.position == edge.ClippedEnds[LR.LEFT]))
			{
				CellCorner c = new CellCorner();
				c.index = corners.Count;
				c.position = edge.ClippedEnds[LR.LEFT];
				c.isBorder = c.position.x == 0 || c.position.x == size.x || c.position.y == 0 || c.position.y == size.y;

				corners.Add(c);
			}

			if (!corners.Any(x => x.position == edge.ClippedEnds[LR.RIGHT]))
			{
				CellCorner c = new CellCorner();
				c.index = corners.Count;
				c.position = edge.ClippedEnds[LR.RIGHT];
				c.isBorder = c.position.x == 0 || c.position.x == size.x || c.position.y == 0 || c.position.y == size.y;

				corners.Add(c);
			}
		}

		//Define some local helper functions to help with the loop below
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

			CellEdge edge = new CellEdge();
			edge.index = edges.Count;

			//Set the voronoi edge
			edge.v0 = corners.First(x => x.position == voronoiEdge.ClippedEnds[LR.LEFT]);
			edge.v1 = corners.First(x => x.position == voronoiEdge.ClippedEnds[LR.RIGHT]);

			//Set the Delaunay edge
			edge.d0 = cells.First(x => x.position == voronoiEdge.LeftSite.Coord);
			edge.d1 = cells.First(x => x.position == voronoiEdge.RightSite.Coord);

			edges.Add(edge);

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
			AddPointToPointList(edge.v0.neighborCorners, edge.v1);
			AddPointToPointList(edge.v1.neighborCorners, edge.v0);

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
		noiseSeed = useCustomSeeds ? noiseSeed : Random.Range(0, 1000); //We use a second, lower seed for the perlin noise because numbers too big can cause problems

		//Define a local helper function to determine if a corner is a land or not
		//We use a perlin noise to determine the shape of the island, but anything can be used. We also use a radius from the center of the map, so the island is always surrounded by water
		bool IsLand(Vector2 position)
		{
			//Normalize the position to a -1 to 1 value
			Vector2 normalizedPosition = new Vector2() {
				x = ((position.x / size.x) - 0.5f) * 2,
				y = ((position.y / size.y) - 0.5f) * 2
			};

			float value = BetterPerlinNoise.SamplePoint(position.x * noiseSize + noiseSeed, position.y * noiseSize + noiseSeed, octaves); //The perlin noise function isn't random, so we need to add a "seed" to offset the values

			//We check if the value of the perlin is greater than the border value
			return value > 0.3f + 0.3f * normalizedPosition.magnitude * normalizedPosition.magnitude; //I don't really understand how this formula works, I just see that 0.3f is the lowest possible value and that it makes a radial patterm since it uses the magnetude of the point
		}

		//Define if a corner is land or not based on some shape function (defined above)

		foreach (var corner in corners)
		{
			corner.isWater = !IsLand(corner.position);
		}
	}

	private void AssignOceanCoastAndLand()
	{
		Queue<CellCenter> queue = new Queue<CellCenter>();
		int numWater = 0;

		//Set the cell to water / ocean / border
		foreach (var center in cells)
		{
			numWater = 0;

			foreach (var corner in center.cellCorners)
			{
				if (corner.isBorder) //If a corner connected to this cell is at the map border, then this cell is a ocean and the corner itself is water
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

			//If the amount of corners on this cell is grater than the defined threshold, this cell is water
			center.isWater = center.isOcean || numWater >= center.cellCorners.Count * LAKE_THRESHOLD;
		}

		//Every cell around a border must be a ocean too, and we loop thought the neighbors until can't find more water (at which case, the queue would be empty)
		while (queue.Count > 0)
		{
			CellCenter c = queue.Dequeue();

			foreach (var n in c.neighborCells)
			{
				if (n.isWater && !n.isOcean)
				{
					n.isOcean = true;
					queue.Enqueue(n); //If this neighbor is a ocean, we add it to the queue so wwe can check its neighbors
				}
			}
		}

		//Set the Coast cells based on the neighbors. If a cell has at least one ocean and one land neighbor, then the cell is a coast
		foreach (var cell in cells)
		{
			int numOcean = 0;
			int numLand = 0;

			foreach (var n in cell.neighborCells)
			{
				numOcean += n.isOcean ? 1 : 0;
				numLand += !n.isWater ? 1 : 0;
			}

			cell.isCoast = numOcean > 0 && numLand > 0;
		}

		//Set the corners attributes based on the connected cells. If all connected cells are ocean, then the corner is ocean. If all cells are land, then the corner is land. Otherwise the corner is a coast
		foreach (var corner in corners)
		{
			int numOcean = 0;
			int numLand = 0;

			foreach (var cell in corner.touchingCells)
			{
				numOcean += cell.isOcean ? 1 : 0;
				numLand += !cell.isWater ? 1 : 0;
			}

			corner.isOcean = numOcean == corner.touchingCells.Count;
			corner.isCoast = numOcean > 0 && numLand > 0;
			corner.isWater = corner.isBorder || numLand != corner.touchingCells.Count && !corner.isCoast;
		}
	}

	private void AssignElevations()
	{
		List<CellCorner> queue = new List<CellCorner>(); //We have to use a List<T> instead of a Queue<T> because we need to add itens both at the begging and a the end of the list
		float minElevation = 1, maxElevation = 1;

		//Find all coast corners and assign their elevation to 0
		foreach (var corner in corners)
		{
			if (corner.isCoast)
			{
				queue.Add(corner);
				corner.elevation = 0;
			}
			else
			{
				corner.elevation = Mathf.Infinity;
			}
		}

		//Define some helper functions to help with the loop below
		bool IsCellLake(CellCenter c)
		{
			return c.isWater && !c.isOcean;
		}

		bool IsEdgeLake(CellEdge e)
		{
			return IsCellLake(e.d0) || IsCellLake(e.d1);
		}

		while (queue.Count > 0)
		{
			CellCorner currentCorner = queue[0]; //Get the fisrt item on the list
			queue.RemoveAt(0); //Remove the item from the list
			int offset = Random.Range(0, currentCorner.connectedEdges.Count); //Add a random offset to the iterator

			for (int i = 0; i < currentCorner.connectedEdges.Count; i++)
			{
				CellEdge e = currentCorner.connectedEdges[(i + offset) % currentCorner.connectedEdges.Count]; //uses the offset to start at a random edge, but still circulate through all of them
				CellCorner neighbor = e.v0 == currentCorner ? e.v1 : e.v0; //Get the corner that is part of this edge and opposite of the current corner
				float newElevation = (IsEdgeLake(e) ? 0 : 1) + currentCorner.elevation;

				//If the neighboor has a higher elevation than the calculated one, we have to change the elevation (in other words, we always use the lowest calculated elevation value)
				if (newElevation < neighbor.elevation)
				{
					neighbor.elevation = newElevation;
					neighbor.downslopeCorner = currentCorner; //Since this elevation is (corner elevation + (0 || 1)), that means this corner is either higher or the same height as the current corner, and so we can set the parent corner as the downslope
					neighbor.downslopeEdge = e;

					//Update the min/max elevations
					if (neighbor.isOcean && newElevation > minElevation)
						minElevation = newElevation;

					if (!neighbor.isOcean && newElevation > maxElevation)
						maxElevation = newElevation;

					//If this corner was a lake, we have to revisit it again to guarantee that all edges of a lake has the same elevation
					if (IsEdgeLake(e))
					{
						queue.Insert(0, neighbor);
					}
					else
					{
						queue.Add(neighbor);
					}
				}
			}
		}

		//Normalize the elevations so we have a range from 0 to 1 for land/lakes, and -1 to 0 for oceans
		foreach (var corner in corners)
		{
			if (!corner.isOcean)
			{
				corner.elevation = elevationCurve.Evaluate(corner.elevation / maxElevation);
			}
			else
			{
				corner.elevation = -elevationCurve.Evaluate(corner.elevation / minElevation);
			}
		}

		//Set the cell center elevation to be the average of its corners. Also, since the coastline is at elevation 0, if some ocean is greater than it, we override the value
		float maxOceanElevation = -0.01f;

		foreach (var center in cells)
		{
			float sumElevations = 0;

			foreach (var corner in center.cellCorners)
			{
				sumElevations += corner.elevation;
			}

			center.elevation = sumElevations / center.cellCorners.Count;

			//make sure that ocean cells won't be on a higher elevation than the coast
			if (center.isOcean && center.elevation > maxOceanElevation)
			{
				center.elevation = maxOceanElevation;
			}
		}
	}

	private void AddRivers()
	{
		List<CellCorner> springs = new List<CellCorner>();

		//Define the min and max elevations from where springs for rivers can start
		float minSpringElevation = 0.3f;
		float maxSpringElevation = 0.9f;

		//Get all corners that can possibly be a spring.
		for (int i = 0; i < corners.Count; i++)
		{
			if(corners[i].elevation >= minSpringElevation && corners[i].elevation <= maxSpringElevation && !corners[i].isWater)
			{
				springs.Add(corners[i]);
			}
		}

		//Select some corners randomly from the previous list to be used as our springs
		List<CellCorner> rivers = new List<CellCorner>();

		springsSeed = useCustomSeeds ? springsSeed : Random.Range(0, 10000);
		Random.InitState(springsSeed);

		while (rivers.Count < numberOfSprings)
		{
			int id = Random.Range(0, springs.Count);
			rivers.Add(springs[id]);
			springs.RemoveAt(id);
		}

		//Assign the flow of the river for each edge using their downslope. Each time a edge is assigned as a river, the water volume is increased by 1, so if two rivers join together, the next edge will have a bigger volume of water
		foreach (var river in rivers)
		{
			CellCorner currentRiverCorner = river;

			while (true)
			{
				//If the current corner doesn't have a downslope, then we reached the coast
				if (currentRiverCorner.downslopeCorner == null)
					break;

				//Increase the water volume for the downslope edge
				currentRiverCorner.downslopeEdge.waterVolume++;

				//Set the current river corner to be the dowsnlope, so we can keep going down until we reach the coast
				currentRiverCorner = currentRiverCorner.downslopeCorner;
			}
		}
	}
	#endregion

	private void OnValidate()
	{
		if(cells != null)
		{
			onMapGenerated?.Invoke();
		}
	}
}
