﻿using UnityEngine;
using UnityEngine.SceneManagement;

using System.Collections.Generic;

using CommSystem;
using Robots;
using UserInterface;
using Utilities;

public class MainForaging : MonoBehaviour, MainInterface
{
    public Camera overheadCamera;
    public ConsoleLine console;
    public GameObject EnvironmentObjects;
    public GameObject Ground;
    public GameObject RobotObjects;
    public GameObject RobotPrefab;
    public GameObject MessageIndicatorPrefab;
    public SceneMaterials sceneMaterials;

    private Config currentConfig;
    private GameObject[] Resources;
    private Queue<string> queuedConsoleCommands;
    private ResourceFactory resourceFactory;
    private Robot[] robots;
    private Satellite Satellite;

    [System.Serializable]
    public class SceneMaterials
    {
        public Material resourceHome;
        public Material resourcePatch;
    }

    /// <summary>
    /// Return the currently active configuration.
    /// </summary>
    /// <returns>The currently active configuration.</returns>
    public Config getCurrentConfig()
    {
        return currentConfig;
    }

    public GameObject getMessageIndicatorPrefab()
    {
        return MessageIndicatorPrefab;
    }

    /// <summary>
    /// Returns the number of robots.
    /// </summary>
    /// <returns>The number of robots.</returns>
    public int getNumRobots()
    {
        return robots.Length;
    }

    /// <summary>
    /// Get a list of all 2D resource positions.
    /// </summary>
    /// <returns>Whether the positions were successfully retrieved.</returns>
    public bool getResourcePositions(out List<Vector2> resourcePositions)
    {
        if (resourceFactory == null)
        {
            resourcePositions = new List<Vector2>();
            return false;
        }
        else
        {
            resourcePositions = resourceFactory.getResourcePositions();
            return true;
        }
    }

    /// <summary>
    /// Get a list of 2D resource positions (only resources in resource cache).
    /// </summary>
    /// <returns>Whether the positions were successfully retrieved.</returns>
    public bool getResourcePositionsInCache(out List<Vector2> resourcePositions)
    {
        if (resourceFactory == null)
        {
            resourcePositions = new List<Vector2>();
            return false;
        }
        else
        {
            List<Vector2> allResourcePositions = resourceFactory.getResourcePositions();
            resourcePositions = new List<Vector2>();

            for (int i = 0; i < allResourcePositions.Count; ++i)
            {
                Vector2 v = allResourcePositions[i];
                if (currentConfig.ResourceHomeRect.Contains(v))
                    resourcePositions.Add(v);
            }

            return true;
        }
    }

    /// <summary>
    /// Get the position of the robot with the given ID.
    /// </summary>
    /// <param name="robotId">The robot ID.</param>
    /// <param name="position">The Vector3 that will be assigned to the robot position.</param>
    /// <returns>Whether the position was successfully assigned.</returns>
    public bool getRobotPosition(uint robotId, out Vector3 position)
    {
        bool result = false;

        if (robotId < robots.Length && robots[robotId] != null)
        {
            position = robots[robotId].body.transform.position;
            result = true;
        }
        else
        {
            position = Vector3.zero;
        }

        return result;
    }

    /// <summary>
    /// Get the current position of the satellite.
    /// </summary>
    /// <returns>Whether the position was successfully assigned.</returns>
    public bool getSatellitePosition(out Vector3 position)
    {
        bool result = false;

        if (Satellite != null && Satellite.body != null)
        {
            position = Satellite.body.transform.position;
            result = true;
        }
        else
        {
            position = Vector3.zero;
        }

        return result;
    }

    /// <summary>
    /// Notify a robot that it has collided with another.
    /// </summary>
    /// <param name="robotId">The id of the robot that collided.</param>
    /// <param name="collision">The Collision object.</param>
    public void notifyCollision(uint robotId, Collision collision)
    {
        robots[robotId].notifyCollision();
    }

    /// <summary>
    /// Notify an actor that it has received a message.
    /// </summary>
    /// <param name="receiverId">The receiver's ID.</param>
    /// <param name="msg">The message.</param>
    public void notifyMessage(uint receiverId, CommMessage msg)
    {
        if (receiverId == Comm.SATELLITE)
            Satellite.queueMessage(msg);
        else
            robots[receiverId].queueMessage(msg);
    }

    /// <summary>
    /// Add a console command to the queue waiting to be processed.
    /// </summary>
    /// <param name="cmd">The command.</param>
    public void queueConsoleCommand(string cmd)
    {
        queuedConsoleCommands.Enqueue(cmd);
    }

    public List<Vector2> refillResourceCache()
    {
        Log.a(LogTag.MAIN, "MainForaging does not yet implement refillResourceCache");
        return new List<Vector2>();
    }

    /// <summary>
    /// Implementation of MonoBehaviour.Start(). Reads the argument files, create the environment, 
    /// place robots, and pause the simulation. 
    /// </summary>
    void Start()
    {
        Log.w(LogTag.MAIN, "Loading scene " + SceneManager.GetActiveScene().name);

        // instantiate user interface
        console.initialize();
        queuedConsoleCommands = new Queue<string>();

        // initialize random number generator
        Random.InitState(System.DateTime.Now.Millisecond);

        // reads args from file, creates default if necessary
        Args args = new Args();

        // create environment (ground and obstacles) and place robots
        initialize(args.configFileName);

        // pause game
        Time.timeScale = 0.0f;
    }

    /// <summary>
    /// Implementation of MonoBehaviour.Update(). Process user input and then call each robot's 
    /// update() function.
    /// </summary>
    void Update()
    {
        // pause/stop the sim, reset the level, etc.
        processUserInput();

        if (Time.timeScale > 0.0f)
        {
            // distribute messages and call each robot's update() function
            updateSim();
        }
    }

    private void OnApplicationQuit()
    {
        ApplicationManager.quit();
    }

    /// <summary>
    /// Create the environment, including initializing and scaling the ground if necessary.
    /// </summary>
    /// <param name="config">The current Config.</param>
    /// <returns>Whether the environment was initialized successfully.</returns>
    private bool generateEnvironment(Config config)
    {
        float scaledGroundLength = config.GroundLength / 10.0f; // plane's dimensions are scaled up
        bool result = true;

        if (scaledGroundLength <= 0f)
        {
            result = false;
        }
        else
        {
            // Create environment header object
            if (EnvironmentObjects == null)
            {
                EnvironmentObjects = GameObject.Find("Environment");
                if (EnvironmentObjects != null)
                {
                    Log.d(LogTag.MAIN, "Located Environment header object in scene");
                }
                else
                {
                    Log.d(LogTag.MAIN, "Created Environment header object");
                    EnvironmentObjects = new GameObject("Environment");
                    EnvironmentObjects.transform.position = Vector3.zero;
                    EnvironmentObjects.transform.rotation = new Quaternion(0f, 0f, 0f, 0f);
                }
            }

            // Create ground
            if (Ground == null)
            {
                Ground = GameObject.Find("Ground");
                if (Ground != null)
                {
                    Log.d(LogTag.MAIN, "Located Ground in scene");
                }
                else
                {
                    Log.d(LogTag.MAIN, "Created Ground");
                    Ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                    Ground.name = "Ground";
                }
            }

            // Set Ground tag to Ground
            Ground.tag = "Ground";

            // Position and scale ground according to configuration
            Ground.transform.localScale = new Vector3(scaledGroundLength,
                                                      1.0f,
                                                      scaledGroundLength);
            Ground.transform.position = Vector3.zero;
            Ground.transform.SetParent(EnvironmentObjects.transform);

            // Add barriers around the ground, assuming it's rectangular
            GameObject positiveX = GameObject.CreatePrimitive(PrimitiveType.Cube);
            positiveX.name = "Positive X";
            positiveX.transform.localScale = new Vector3(0.1f, 0.25f, config.GroundLength + 0.2f);
            positiveX.transform.position = new Vector3(config.GroundLength / 2.0f + 0.05f,
                                                       0.125f,
                                                       0.0f);
            positiveX.transform.parent = EnvironmentObjects.transform;

            GameObject negativeX = GameObject.CreatePrimitive(PrimitiveType.Cube);
            negativeX.name = "Negative X";
            negativeX.transform.localScale = new Vector3(0.1f, 0.25f, config.GroundLength + 0.2f);
            negativeX.transform.position = new Vector3(config.GroundLength / -2.0f - 0.05f,
                                                       0.125f,
                                                       0.0f);
            negativeX.transform.parent = EnvironmentObjects.transform;

            GameObject positiveZ = GameObject.CreatePrimitive(PrimitiveType.Cube);
            positiveZ.name = "Positive Z";
            positiveZ.transform.localScale = new Vector3(config.GroundLength + 0.2f, 0.25f, 0.1f);
            positiveZ.transform.position = new Vector3(0.0f,
                                                       0.125f,
                                                       config.GroundLength / 2.0f + 0.05f);
            positiveZ.transform.parent = EnvironmentObjects.transform;

            GameObject negativeZ = GameObject.CreatePrimitive(PrimitiveType.Cube);
            negativeZ.name = "Negative Z";
            negativeZ.transform.localScale = new Vector3(config.GroundLength + 0.2f, 0.25f, 0.1f);
            negativeZ.transform.position = new Vector3(0.0f,
                                                       0.125f,
                                                       config.GroundLength / -2.0f - 0.05f);
            negativeZ.transform.parent = EnvironmentObjects.transform;

            // Create satellite
            GameObject satelliteBody = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            satelliteBody.tag = "Satellite";
            satelliteBody.name = "Satellite";
            satelliteBody.transform.parent = EnvironmentObjects.transform;
            satelliteBody.transform.position = new Vector3(0, 15, 0);
            Satellite = new Satellite(satelliteBody, this);

            // Create resources
            resourceFactory = new ResourceFactory();
            uint startId = 0;
            for (uint i = 0; i < 3; i++)
            {
                // random resource generator code
                //avoid the center be in the center of the Robots home
                //uint sideLength = (uint)(Random.Range(1, 4));
                //startId = resourceFactory.createResourcePatch(i, sideLength * sideLength, startId, center, 1.5f, 1.0f, Color.blue, sceneMaterials.resourcePatch);

                // only adds 3 resource batches
                Vector2 center = new Vector2(Random.Range(-25.0f, 25.0f), Random.Range(-25.0f, 25.0f));
                uint sideLength = 3;
                startId = resourceFactory.createResourcePatch(i, sideLength * sideLength, startId, center, 1.5f, 1.0f, Color.blue, sceneMaterials.resourcePatch);
            }

            // Create Resource Home
            WorldspaceUIFactory.createQuad("Resource Home", new Rect(new Vector2(17, 17), new Vector2(10, 10)), sceneMaterials.resourceHome);
        }

        return result;
    }

    /// <summary>
    /// Generate the environment and initialize the robots.
    /// </summary>
    /// <param name="configFile">The name of the config file.</param>
    /// <returns>Whether initialization completed successfully.</returns>
    private bool initialize(string configFile)
    {
        bool result = true;
        currentConfig = new Config(configFile); // reads the config file and sets parameters

        if (!generateEnvironment(currentConfig) || !placeRobots(currentConfig))
        {
            result = false;
            Log.a(LogTag.MAIN, "Failed to initialize scene using " + configFile);
        }
        else
        {
            // set overhead camera to see whole scene, etc
            repositionCameras(currentConfig);
        }

        return result;
    }

    /// <summary>
    /// Initialize and place the robots according to the configuration.
    /// </summary>
    /// <param name="config">The current Config.</param>
    /// <returns>Whether the robots were initialized and positioned successfully.</returns>
    private bool placeRobots(Config config)
    {
        // TODO: read number, placement shape, and location from config

        bool result = true;

        // Create robot header object
        if (RobotObjects == null)
        {
            RobotObjects = GameObject.Find("Robots");
            if (RobotObjects != null)
            {
                Log.d(LogTag.MAIN, "Located Robots header object in scene");
            }
            else
            {
                Log.d(LogTag.MAIN, "Created Robots header object");
                RobotObjects = new GameObject("Robots");
                RobotObjects.transform.position = Vector3.zero;
                RobotObjects.transform.rotation = new Quaternion(0f, 0f, 0f, 0f);
            }
        }


        if (config.SpawnShape == Config.eSpawnShape.SQUARE)
        {
            uint numRobotsRoot = (uint)Mathf.Sqrt(config.NumRobots);
            robots = new Robot[numRobotsRoot * numRobotsRoot];

            if (numRobotsRoot == 1)
            {
                Vector3 position = new Vector3(config.SpawnCenter.x,
                                               RobotPrefab.transform.position.y,
                                               config.SpawnCenter.y);

                robots[0] = new Robot(0,
                                      Instantiate(RobotPrefab),
                                      position,
                                      0.0f,
                                      config.RobotRadarRange);
            }
            else
            {
                float robotSpacing = config.SpawnRadius * 2.0f / (numRobotsRoot - 1);
                for (uint i = 0; i < numRobotsRoot; ++i)
                {
                    for (uint j = 0; j < numRobotsRoot; ++j)
                    {
                        float x = config.SpawnCenter.x - config.SpawnRadius + i * robotSpacing;
                        float z = config.SpawnCenter.y - config.SpawnRadius + j * robotSpacing;
                        uint id = numRobotsRoot * i + j;
                        Vector3 position = new Vector3(x, RobotPrefab.transform.position.y, z);
                        robots[numRobotsRoot * i + j] = new Robot(id,
                                                                  Instantiate(RobotPrefab),
                                                                  position,
                                                                  0.0f,
                                                                  config.RobotRadarRange);
                    }
                }
            }

        }
        else
        {
            Log.a(LogTag.MAIN, "Spawn shape must be a square.");
        }

        return result;
    }

    private void processConsoleCommands()
    {
        while (queuedConsoleCommands.Count > 0)
        {
            string cmd = queuedConsoleCommands.Dequeue().ToLower();

            if (cmd == "construction")
            {
                Satellite.broadcastMessage("construction\tstart");
            }
            else if (cmd == "forage")
            {
                Satellite.startForaging();
            }
            else if (cmd == "test_message")
            {
                uint sender = (uint)Random.Range(0, robots.Length);
                uint receiver = sender;
                while (receiver == sender)
                    receiver = (uint)Random.Range(0, robots.Length + 1);

                if (receiver == robots.Length) // broadcast message
                {
                    Comm.broadcastMessage(sender, "TEST BROADCAST");
                }
                else // direct message
                {
                    Comm.directMessage(sender, receiver, "TEST DIRECT MESSAGE");
                }
            }
            else if (cmd == "test_queue")
            {
                Satellite.broadcastMessage(cmd);
            }
            else
            {
                Log.e(LogTag.MAIN, "Unknown console command: " + cmd);
            }
        }
    }

    /// <summary>
    /// Get user input,  perform application functions (quit, pause, reload, etc.).
    /// </summary>
    private void processUserInput()
    {
        processConsoleCommands();

        if (Input.GetKeyDown(KeyCode.BackQuote))
        {
            console.toggle();
        }

        if (!console.isSelected())
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                ApplicationManager.quit();
            }
            else if (Input.GetKeyDown(KeyCode.R))
            {
                ApplicationManager.reloadScene();
            }
            else if (Input.GetKeyDown(KeyCode.P) || Input.GetKeyDown(KeyCode.Space))
            {
                ApplicationManager.togglePause();
                Log.d(LogTag.MAIN, "Timescale set to " + Time.timeScale);
            }
            else if (Input.GetKeyDown(KeyCode.C))
            {
                Comm.toggleShowInUnityConsole();
            }
            else if (Input.GetKeyDown(KeyCode.I))
            {
                Comm.toggleShowMsgIndicators();
            }
        }
    }

    /// <summary>
    /// Resize the overhead camera to fit the entire scene.
    /// </summary>
    /// <param name="config">The current Config.</param>
    private void repositionCameras(Config config)
    {
        if (overheadCamera == null)
        {
            Log.e(LogTag.MAIN, "Reference to overhead camera is NULL");
        }
        else
        {
            overheadCamera.transform.position = new Vector3(config.GroundLength,
                                                            0.8f * config.GroundLength,
                                                            -1.0f * config.GroundLength);
            overheadCamera.orthographic = true;
            overheadCamera.orthographicSize = config.GroundLength * 0.42f;
        }
    }

    /// <summary>
    /// Iterate through all the robots and call their update() method.
    /// </summary>
    private void updateSim()
    {
        Comm.update(Time.deltaTime);
        Satellite.update();

        for (int i = 0; i < robots.Length; ++i)
            robots[i].update();
    }
}
