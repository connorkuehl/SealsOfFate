﻿using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using Assets.Scripts;
using Assets.Scripts.LevelGeneration;
using Assets.Scripts.Utility;
using UnityEngine;
using UnityEngine.Assertions;
using Debug = System.Diagnostics.Debug;

//Copyright 2017 Legit Buisness, LLC, Andrew Waugh

/// <summary>
///     Runs a search with a given start and end on a given searchMap and returns a list of vector location waypoints
///     to reach the destination.
/// </summary>
public class SearchAStar {
    private readonly Vector2 _end;
    private readonly PathHeuristic _heuristic;
    private readonly MovingObject _seeker;
    private readonly Vector2 _start;
    private readonly bool _debugMode = true;

    /// <summary>
    ///     Ctor for the A* search. It requires a searchMap from the level, a world vector start and end,
    ///     and a heuristic strategy object.
    /// </summary>
    /// <param name="seeker">The entity looking for the path</param>
    /// <param name="start">A vector2 to the starting location. Must be in worldspace</param>
    /// <param name="end">A vector2 to the ending location. Must be in worldspace</param>
    /// <param name="heuristic">A heurstic strategy object</param>
    public SearchAStar(MovingObject seeker, Vector2 start, Vector2 end, PathHeuristic heuristic) {
        _seeker = seeker;
        _start = start;
        _end = end;
        _heuristic = heuristic;
    }

    /// <summary>
    /// The main method call for the A* search.
    /// </summary>
    /// <returns>A list of edges. edge.destination contains the world vector2 that each edge points to.</returns>
    public List<Edge> Search() {
        var startRecord = new NodeRecord {
            Location = _start,
            Connection = null,
            CostSoFar = 0,
            EstimatedTotalCost = _heuristic.Estimate(_start)
        };

        //TODO Replace with a priority queue
        var openList = new BucketQueue<NodeRecord>();
        var closedList = new BucketQueue<NodeRecord>();

        openList.Enqueue(startRecord,(int)startRecord.EstimatedTotalCost);

        NodeRecord current = null;
        Assert.raiseExceptions = true;
        while (openList.Count > 0) {
            Assert.IsFalse(openList.Count > 50000,"OpenList has an insane number of nodes");
            current = openList.Peek();

            if (openList.Count % 1000 == 0) {
                UnityEngine.Debug.LogWarning("<i>Pathfinding:</i> There are too many nodes in the open list. >" + openList.Count);
                //openList.LogContents();
            }
            if (closedList.Count > 0 && closedList.Count % 1000 == 0) { 
                UnityEngine.Debug.LogWarning("<i>Pathfinding:</i> There are too many nodes in the closed list. >" + closedList.Count);
                //closedList.LogContents();
            }

            //If we're at the goal, end early
            if (current.Location.Equals(_end)) {
                break;
            }

            //If we're far away, end early
            if ((_end - current.Location).sqrMagnitude > 500f)
            {
                //We are very far away from the destination. It's likely we won't be able to reach it.
                //Let's not waste performance.
                UnityEngine.Debug.DrawLine(_start, _end, Color.gray, 300f);
                UnityEngine.Debug.Log("<i>Pathfinding:</i> Pathfinding has reached a node that is too far away. Aborting. Distance: " + (current.Location - _end).magnitude);
                return null;
            }

            //Otherwise, get the connections
            var connections = GetConnections(current);
            NodeRecord endNodeRecord;
            Vector2 endLoc;
            float endCost;
            float endHeuristic;
            foreach (var con in connections) {
                endLoc = con.Destination;
                endCost = current.CostSoFar + con.Cost;

                if (_debugMode) {
                    UnityEngine.Debug.DrawLine(con.From, con.Destination, Color.blue,2,false);
                }

                //If the node is closed, we may have to skip or remove from the closed list
                if (closedList.Any(closedRecord => closedRecord.Location == endLoc)) {
                    Assert.IsNotNull(closedList,"Closed List should not be null.");
                    Assert.IsFalse(closedList.Count == 0,"Closed List should not be empty");
                    endNodeRecord =
                        closedList.First(closedRecord => closedRecord.Location.Equals(endLoc)); //Retrieve the record we found
                    if (endNodeRecord.CostSoFar <= endCost) {
                        //If this route isn't shorter, then skip.
                        continue;
                    }
                    //Otherwise, remove it from the closed list
                    closedList.Remove(endNodeRecord,(int)endNodeRecord.EstimatedTotalCost);
                    //Recalculate the heuristic. TODO: recalculate using old values
                    endHeuristic = _heuristic.Estimate(endLoc);
                }
                else if (openList.Any(openRecord => openRecord.Location == endLoc)) {
                    //Skip if the node is open and we haven't found a better route
                    endNodeRecord = openList.First(openRecord => openRecord.Location == endLoc);

                    if (endNodeRecord.CostSoFar <= endCost) {
                        continue;
                    }
                    //Recalculate the heuristic
                    endHeuristic = _heuristic.Estimate(endLoc);
                }
                else {
                    //Otherwise, we're on an unvisited node that needs a new record
                    endNodeRecord = new NodeRecord {Location = endLoc};
                    endHeuristic = _heuristic.Estimate(endLoc);
                }
                //If we reached this point, it means we need to update the node
                endNodeRecord.CostSoFar = endCost;
                endNodeRecord.Connection = con; //remember: we're iterating through the connections right now
                endNodeRecord.EstimatedTotalCost = endCost + endHeuristic;

                //If this record isn't in the openlist already
                if (openList.All(openRecord => openRecord.Location != endLoc)) {
                    openList.Enqueue(endNodeRecord, (int) endNodeRecord.EstimatedTotalCost);
                }
            }
            //Finished looking at the connections, move it to the closed list.
            openList.Remove(current,(int)current.EstimatedTotalCost);
            closedList.Enqueue(current,(int)current.EstimatedTotalCost);
        }
        Assert.IsNotNull(current, "current != null");
        if (current.Location != _end) {
            //We're out of nodes and haven't found the goal. No solution.
            UnityEngine.Debug.DrawLine(current.Location,_end,Color.black,200f);
            return null;
        }
        //We found the path, time to compile a list of connections
        var outputList = new List<Edge>(20);

        while (current.Location != _start) {
            if (_debugMode) {
                UnityEngine.Debug.DrawLine(current.Connection.From, current.Connection.Destination, Color.red, 2, false);
            }
            outputList.Add(current.Connection);
            current = current.Connection.PreviousRecord;
        }
        outputList.Reverse();
        return outputList;
    }

    /// <summary>
    /// Generates a list of edges connected to a location given in a tile record.
    /// </summary>
    /// <param name="tileRecord">The location record to get connections from</param>
    /// <returns>A list of at most 4 edges connected to this tile record.</returns>
    private List<Edge> GetConnections(NodeRecord tileRecord) {
        var worldCoordinate = tileRecord.Location;
        var retList = new List<Edge>(4);
        var blockingLayer = LayerMask.GetMask("Blocking");
        _seeker.GetComponent<BoxCollider2D>().enabled = false;

        //TODO: Optimize: reduce the number of new vector2s created
        //TODO: Generalize the collision exceptions
        for (int x = -1; x <= 1; ++x) {
            for (int y = -1; y <= 1; ++y) {
                if ((x != 0 && y != 0) || (x == 0 && y == 0)) {
                    continue;
                }
                RaycastHit2D hit2D = Physics2D.Linecast(worldCoordinate,
                    new Vector2(worldCoordinate.x + x, worldCoordinate.y + y), blockingLayer);
                if (!hit2D || hit2D.transform.CompareTag("Player"))
                {
                    retList.Add(new Edge(worldCoordinate, new Vector2(worldCoordinate.x + x, worldCoordinate.y+y), tileRecord));
                }
            }
        }
        _seeker.GetComponent<BoxCollider2D>().enabled = true;
        return retList;
    }

    /// <summary>
    /// An internal structure used to keep track of nodes/tiles that have been visited and the calculated/total costs
    /// for each.
    /// </summary>
    public class NodeRecord {
        /// <summary>The connection taken from this node</summary>
        public Edge Connection;
        /// <summary>The total weighted cost so far to reach this node</summary>
        public float CostSoFar;
        /// <summary>The estimated total cost from here to the end given by the heuristic</summary>
        public float EstimatedTotalCost;
        /// <summary>The world location of this node</summary>
        public Vector2 Location;

        public override string ToString() {
            return "Node At " + Location + "Estimated/Measured cost" + EstimatedTotalCost + "/" + CostSoFar;
        }
    }

    /// <summary>
    /// A connection between two nodes/tiles.
    /// </summary>
    public class Edge {
        /// <summary>The weighted cost/distance of this edge. Currently, there are no possible modifiers, so this
        /// is always 1</summary>
        public float Cost = 1;
        /// <summary>The world location that this edge connects to</summary>
        public Vector2 Destination;
        /// <summary>The world location that this edge connects from</summary>
        public Vector2 From;
        /// <summary>Internal. The previous record associated with From. Used to find the path back from the goal</summary>
        public NodeRecord PreviousRecord;

        /// <summary>
        /// Ctor for an edge.
        /// </summary>
        /// <param name="from">The world space location that the connection originates from.</param>
        /// <param name="destination">The world space location that connection heads to</param>
        /// <param name="previousRecord">The previous record that we used to reach this connection</param>
        public Edge(Vector2 from, Vector2 destination, NodeRecord previousRecord) {
            From = from;
            Destination = destination;
            PreviousRecord = previousRecord;
        }
    }
}

/// <summary>
/// PathHeuristic is an abstract base class for the strategy used to calculate the heuristic estimates in A*.
/// To implement your own, extend this class and override the estimate method.
/// </summary>
public abstract class PathHeuristic {

    /// <summary>The world space location of the final goal for the A* algorithm.</summary>
    protected Vector2 _goalLocation;

    /// <summary>
    /// Default Ctor. Don't use this.
    /// </summary>
    protected PathHeuristic() { }

    /// <summary>
    /// CTor for PathHeuristic objects. Takes in the goal location.
    /// </summary>
    /// <param name="goalLocation">The worldspace location of the final goal for A*</param>
    public PathHeuristic(Vector2 goalLocation) {
        _goalLocation = goalLocation;
    }

    /// <summary>
    /// Abstract function that should return the estimated distance between the passed in location and the end goal.
    /// </summary>
    /// <param name="startFrom">Worldspace location of where to start from</param>
    /// <returns></returns>
    public abstract float Estimate(Vector2 startFrom);
}

/// <summary>
/// Heuristic that estimates the distance between the start and end locations by finding the difference in both the x
/// and y coordinates and then adding the absolute value of them.
/// </summary>
public class ManhattanDistance : PathHeuristic {
    public ManhattanDistance(Vector2 goalLocation) : base(goalLocation) {
        
    }
    public override float Estimate(Vector2 startFrom) {
        return Mathf.Abs(startFrom.x - _goalLocation.x)
               + Mathf.Abs(startFrom.y - _goalLocation.y);
    }
}
