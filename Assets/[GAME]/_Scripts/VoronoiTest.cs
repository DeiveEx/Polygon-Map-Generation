using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using csDelaunay;

public class VoronoiTest : MonoBehaviour
{
    // The number of polygons/sites we want
    public int polygonNumber = 200;
    public int relaxation = 0;

    // This is where we will store the resulting data
    private Dictionary<Vector2, Site> sites;
    private List<Edge> edges;

    void Start()
    {
        // Create your sites (lets call that the center of your polygons)
        List<Vector2> points = CreateRandomPoint();

        // Create the bounds of the voronoi diagram
        // Use Rectf instead of Rect; it's a struct just like Rect and does pretty much the same,
        // but like that it allows you to run the delaunay library outside of unity (which mean also in another tread)
        Rectf bounds = new Rectf(0, 0, 512, 512);

        // There is a two ways you can create the voronoi diagram: with or without the lloyd relaxation
        // Here I used it with 2 iterations of the lloyd relaxation
        Voronoi voronoi = new Voronoi(points, bounds, relaxation);

        // But you could also create it without lloyd relaxtion and call that function later if you want
        //Voronoi voronoi = new Voronoi(points,bounds);
        //voronoi.LloydRelaxation(5);

        // Now retreive the edges from it, and the new sites position if you used lloyd relaxtion
        sites = voronoi.SitesIndexedByLocation;
        edges = voronoi.Edges;

        DisplayVoronoiDiagram();
    }

    private List<Vector2> CreateRandomPoint()
    {
        // Use Vector2, instead of Vector2
        // Vector2 is pretty much the same than Vector2, but like you could run Voronoi in another thread
        List<Vector2> points = new List<Vector2>();
        for (int i = 0; i < polygonNumber; i++)
        {
            points.Add(new Vector2(Random.Range(0, 512), Random.Range(0, 512)));
        }

        return points;
    }

    // Here is a very simple way to display the result using a simple bresenham line algorithm
    // Just attach this script to a quad
    private void DisplayVoronoiDiagram()
    {
        Texture2D tx = new Texture2D(512, 512);
        foreach (KeyValuePair<Vector2, Site> kv in sites)
        {
            tx.SetPixel((int)kv.Key.x, (int)kv.Key.y, Color.red);
        }
        foreach (Edge edge in edges)
        {
            // if the edge doesn't have clippedEnds, if was not within the bounds, dont draw it
            if (edge.ClippedEnds == null) continue;

            DrawLine(edge.ClippedEnds[LR.LEFT], edge.ClippedEnds[LR.RIGHT], tx, Color.black);
        }
        tx.Apply();

        this.GetComponent<Renderer>().material.mainTexture = tx;
    }

    // Bresenham line algorithm
    private void DrawLine(Vector2 p0, Vector2 p1, Texture2D tx, Color c, int offset = 0)
    {
        int x0 = (int)p0.x;
        int y0 = (int)p0.y;
        int x1 = (int)p1.x;
        int y1 = (int)p1.y;

        int dx = Mathf.Abs(x1 - x0);
        int dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            tx.SetPixel(x0 + offset, y0 + offset, c);

            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 > -dy)
            {
                err -= dy;
                x0 += sx;
            }
            if (e2 < dx)
            {
                err += dx;
                y0 += sy;
            }
        }
    }
}
