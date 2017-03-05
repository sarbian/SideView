/*
The MIT License (MIT)

Copyright (c) 2017 Sarbian

Permission is hereby granted, free of charge, to any person obtaining a copy of
this software and associated documentation files (the "Software"), to deal in
the Software without restriction, including without limitation the rights to
use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER
IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN
CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/


using System;
using System.Collections.Generic;
using KSP.IO;
using UnityEngine;
using Upgradeables;

namespace SideView
{
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class SideView : MonoBehaviour
    {
        [Persistent]
        public int displayIdx = 1;
        [Persistent]
        public int displayWidth = 0;
        [Persistent]
        public int displayHeight = 0;
        [Persistent]
        public int displayHz = 60;
        private bool isInit = false;

        private GameObject focus;
        private GameObject cameraObject;
        private GameObject galaxyCameraObject;
        private GameObject orbitCameraObject;
        private Camera camera;
        private Camera galaxycamera;
        private Camera orbitCamera;

        private Color savedAmbientLight;
        private FollowRot followRot;

        private float distance = 200;
        private float minDistance;
        private float camHdg;
        private float camPitch;

        private Renderer[] cubeRenderers;
        private SunCoronas[] sunCoronas;
        private MaterialPropertyBlock mpb;
        private readonly List<MeshRenderer> rendererList = new List<MeshRenderer>();

        private bool isInMap = false;

        private static Material material;

        private int targetIdx = -1;


        void Start()
        {
            DontDestroyOnLoad(this);

            print("Found " + Display.displays.Length + " displays");

            mpb = new MaterialPropertyBlock();
            material = new Material(Shader.Find("Particles/Additive"));
            
            if (File.Exists<SideView>("config.cfg"))
            {
                ConfigNode config = ConfigNode.Load(IOUtils.GetFilePathFor(this.GetType(), "config.cfg"));
                ConfigNode.LoadObjectFromConfig(this, config);
            }

            GameEvents.onLevelWasLoadedGUIReady.Add(LevelWasLoaded);

            Camera.onPreCull += onPreCull;
            Camera.onPreRender += onPreRender;
            Camera.onPostRender += onPostRender;
        }

        void OnDestroy()
        {
            ConfigNode config = new ConfigNode("SideView");
            ConfigNode.CreateConfigFromObject(this, config);
            config.Save(IOUtils.GetFilePathFor(this.GetType(), "config.cfg"));
        }

        // In a magical world where Unity actually works as documented this should work.
        // In the real work it does not and always report z = 0...
        // However it does work in Unity 5.5...
        //void OnGUI()
        //{
        //    var mousePos = Display.RelativeMouseAt(Input.mousePosition);
        //    GUI.Label(new Rect(10, 70, 300, 20), "mouse position:" + (Vector2) Input.mousePosition);
        //    GUI.Label(new Rect(10, 90, 300, 20), "mouse position:" + (Vector2) mousePos);
        //    GUI.Label(new Rect(10, 110, 300, 20), "display id:" + mousePos.z);
        //}

        private void Init()
        {
            if (isInit || Display.displays.Length == 1 || Application.isEditor)
                return;

            if (displayIdx >= Display.displays.Length)
            {
                print("Asked display " + displayIdx + " does not exist");
                return;
            }

            print("Display " + displayIdx + " " + Display.displays[displayIdx].systemWidth + "x" + Display.displays[displayIdx].systemHeight + " " +
                  Display.displays[displayIdx].renderingWidth + "x" + Display.displays[displayIdx].renderingHeight);

            print("Display Setting Activate");

            //Display.displays[displayIdx].Activate();
            Display.displays[displayIdx].Activate(displayWidth, displayHeight, displayHz);
            
            isInit = true;

            LevelWasLoaded(HighLogic.LoadedScene);
        }

        private void LevelWasLoaded(GameScenes data)
        {
            if (!isInit || data != GameScenes.FLIGHT)
                return;

            if (!focus)
            {
                focus = new GameObject("SideViewFocus");
                focus.transform.parent = FlightGlobals.ActiveVessel.mapObject.transform; // or one of PlanetariumCamera.fetch.targets
                focus.transform.localPosition = Vector3.zero;
                focus.transform.localRotation = Quaternion.identity;
            }

            if (!cameraObject)
            {
                cameraObject = new GameObject("SideViewCameraScaledSpace");
                cameraObject.transform.parent = focus.transform;

                cameraObject.transform.localPosition = Vector3.back * 40;
                cameraObject.transform.localRotation = Quaternion.identity;

                camera = cameraObject.AddComponent<Camera>();
                camera.CopyFrom(PlanetariumCamera.Camera);
                camera.targetDisplay = 1;
                // We want to render last so I can play with setting and don't worry about breaking the stock renders. *Cross fingers*
                camera.depth = 6;
                camera.cullingMask = camera.cullingMask & ~(1 << 31); // Do not render the stock orbits
                camera.cullingMask = camera.cullingMask & ~(1 << LayerMask.NameToLayer("Atmosphere")); // Do not render the Atmosphere

                cameraObject.AddComponent<GUILayer>();
                //cameraObject.AddComponent<FlareLayer>(); // Funny effects when enabled...


                sunCoronas = Planetarium.fetch.Sun.scaledBody.GetComponentsInChildren<SunCoronas>();
            }

            // Did not work as expected but draw the planet/orbit interception better
            //if (!orbitCameraObject)
            //{
            //    // Orbit camera 
            //    orbitCameraObject = new GameObject("SideViewOrbitCamera");
            //    orbitCameraObject.transform.parent = cameraObject.transform;
            //    
            //    orbitCameraObject.transform.localPosition = Vector3.zero;
            //    orbitCameraObject.transform.localRotation = Quaternion.identity;
            //    
            //    orbitCamera = orbitCameraObject.AddComponent<Camera>();
            //    orbitCamera.CopyFrom(PlanetariumCamera.Camera);
            //    orbitCamera.targetDisplay = 1;
            //    orbitCamera.depth = 7;
            //    orbitCamera.cullingMask = 0; // Do not render anything
            //}


            if (!galaxyCameraObject)
            {
                // Skybox Camera
                Transform galaxyRoot = GalaxyCubeControl.Instance.transform.parent;
                Camera stockGalaxyCamera = galaxyRoot.FindChild("GalaxyCamera").GetComponent<Camera>();
                galaxyCameraObject = new GameObject("SideViewGalaxyCamera");
                galaxyCameraObject.transform.parent = galaxyRoot;

                galaxycamera = galaxyCameraObject.AddComponent<Camera>();
                galaxycamera.CopyFrom(stockGalaxyCamera);
                galaxycamera.targetDisplay = 1;
                galaxycamera.depth = 4;

                galaxyCameraObject.transform.localPosition = Vector3.zero;
                galaxyCameraObject.transform.localRotation = Quaternion.identity;

                followRot = galaxyCameraObject.AddComponent<FollowRot>();
                followRot.tgt = cameraObject.transform;
                followRot.followX = true;
                followRot.followY = true;
                followRot.followZ = true;

                galaxyCameraObject.AddComponent<FollowRotEnabler>();

                cubeRenderers = GalaxyCubeControl.Instance.GetComponentsInChildren<Renderer>();
            }

            targetIdx = -1;

            // Fix the launchpad watertower spotlight so that it does not light scaledSpace
            foreach (UpgradeableFacility facility in GameObject.FindObjectsOfType<UpgradeableFacility>())
            {
                if (facility.id == "SpaceCenter/LaunchPad")
                {
                    Light[] lights = facility.transform.GetComponentsInChildren<Light>();

                    foreach (Light light in lights)
                    {
                        if (light.name == "Spotlight")
                        {
                            print("Fixing the launchpad watertower spotlight");
                            light.cullingMask = light.cullingMask & ~LayerMask.NameToLayer("Scaled Scenery");
                        }
                    }
                }
            }
        }
        
        public void LateUpdate()
        {
            if (!cameraObject && Input.GetKeyDown(KeyCode.Keypad0))
                Init();

            if (cameraObject)
            {
                if (Input.GetKeyDown(KeyCode.Keypad0))
                {
                    isInMap = !isInMap;
                    
                    if (isInMap)
                    {
                        InputLockManager.SetControlLock(ControlTypes.CAMERACONTROLS, "SideView");
                    }
                    else
                    {
                        InputLockManager.RemoveControlLock("SideView");
                    }
                }

                int prevTargetIdx = targetIdx;

                if (isInMap && GameSettings.CAMERA_RESET.GetKeyDown())
                {
                    ResetTarget();
                }

                if (isInMap && Input.GetKeyDown(KeyCode.Tab) && !Input.GetKey(KeyCode.LeftShift))
                {
                    targetIdx = (targetIdx + 1) % PlanetariumCamera.fetch.targets.Count;
                }
                
                if (isInMap && Input.GetKeyDown(KeyCode.Tab) && Input.GetKey(KeyCode.LeftShift))
                {
                    targetIdx = (targetIdx - 1) % PlanetariumCamera.fetch.targets.Count;
                }

                if (targetIdx < 0 || targetIdx >= PlanetariumCamera.fetch.targets.Count)
                    ResetTarget();

                if (prevTargetIdx != targetIdx || focus.transform.parent == null)
                {
                    MapObject target = PlanetariumCamera.fetch.targets[targetIdx];
                    focus.transform.parent = target.transform;

                    if (target.vessel != null)
                        minDistance = (float) (0.5 * target.vessel.orbit.semiMajorAxis * ScaledSpace.InverseScaleFactor);

                    if (target.celestialBody != null)
                        minDistance = (float)(1.5 * target.celestialBody.sphereOfInfluence * ScaledSpace.InverseScaleFactor);

                    minDistance = Mathf.Max(minDistance, PlanetariumCamera.fetch.minDistance);
                }

                focus.transform.localPosition = Vector3.zero;

                if (isInMap && GameSettings.AXIS_MOUSEWHEEL.GetAxis() != 0.0f)
                {
                    distance = distance * (1f - (GameSettings.AXIS_MOUSEWHEEL.GetAxis() * 1.2f));
                }
                
                distance = Mathf.Clamp(distance, minDistance, PlanetariumCamera.fetch.maxDistance);

                cameraObject.transform.localPosition = distance * Vector3.back;
                cameraObject.transform.localRotation = Quaternion.identity;

                if (isInMap && CameraMouseLook.GetMouseLook())
                {
                    camHdg = camHdg + Input.GetAxis("Mouse X") * PlanetariumCamera.fetch.orbitSensitivity;
                    camPitch = camPitch - Input.GetAxis("Mouse Y") * PlanetariumCamera.fetch.orbitSensitivity;
                }

                camPitch = Mathf.Clamp(camPitch, -0.5f * Mathf.PI, 0.5f * Mathf.PI);

                Quaternion hdgRot = Quaternion.AngleAxis(camHdg * Mathf.Rad2Deg + (float)Planetarium.InverseRotAngle, Vector3.up);
                Quaternion pitchRot = Quaternion.AngleAxis(camPitch * Mathf.Rad2Deg, Vector3.right);
                focus.transform.rotation = hdgRot * pitchRot;
            }
        }

        private void ResetTarget()
        {
            if (FlightGlobals.ActiveVessel != null)
            {
                targetIdx = PlanetariumCamera.fetch.targets.IndexOf(FlightGlobals.ActiveVessel.mapObject);
            }
            else
            {
                targetIdx = PlanetariumCamera.fetch.targets.IndexOf(Planetarium.fetch.Home.MapObject);
            }
        }
        
        private void onPreCull(Camera cam)
        {
            if (cam == camera)
            {
                rendererList.Clear();
                ScaledSpace.Instance.GetComponentsInChildren(rendererList);
                foreach (MeshRenderer rend in rendererList)
                {
                    if (!rend.enabled)
                    {
                        rend.enabled = true;
                    }
                }
            }
        }

        private void onPreRender(Camera cam)
        {
            if (cam == camera)
            {
                savedAmbientLight = RenderSettings.ambientLight;
                RenderSettings.ambientLight = DynamicAmbientLight.Instance.vacuumAmbientColor;

                foreach (SunCoronas corona in sunCoronas)
                {
                    corona.transform.rotation = Quaternion.LookRotation(cam.transform.position - corona.transform.position, Vector3.up);
                }
            }
            else if (cam == galaxycamera)
            {
                // TODO : Galaxy intensity changes with position / occlusion

                mpb.SetColor(PropertyIDs._Color, GalaxyCubeControl.Instance.maxGalaxyColor);

                for (int i = 0; i < cubeRenderers.Length; ++i)
                    cubeRenderers[i].SetPropertyBlock(mpb);
            }
        }

        private void onPostRender(Camera cam)
        {
            if (cam == camera)
            {
                RenderSettings.ambientLight = savedAmbientLight;
                DrawOrbits(cam);
            }
            //else if (cam == orbitCamera)
            //{
            //    DrawOrbits(cam);
            //}
        }

        private static void DrawOrbits(Camera cam)
        {
            GL.PushMatrix();

            GL.LoadProjectionMatrix(cam.projectionMatrix);
            GL.modelview = cam.worldToCameraMatrix;

            bool showOrbits =
                GameVariables.Instance.GetOrbitDisplayMode(ScenarioUpgradeableFacilities.GetFacilityLevel(SpaceCenterFacility.TrackingStation)) >=
                GameVariables.OrbitDisplayMode.AllOrbits;

            double degStep = 2;
            int pointsCount = (int)Math.Floor(360d / degStep);
            double radStep = degStep * UtilMath.Deg2Rad;

            foreach (OrbitDriver orbitDriver in Planetarium.Orbits)
            {
                //Vessel vessel = orbitDriver.vessel;
                //if (vessel != null)
                //{
                //    
                //    print(vessel.name + " " + orbitDriver.updateMode + " " 
                //        + orbitDriver.Renderer.drawMode + " " 
                //        + MapViewFiltering.CheckAgainstFilter(vessel) + " " 
                //        + (vessel.vesselType == VesselType.Debris && !orbitDriver.Renderer.isFocused));
                //}

                if (!orbitDriver.Renderer || orbitDriver.Renderer.discoveryInfo == null)
                    continue;

                bool visible = showOrbits
                               && orbitDriver.Renderer.discoveryInfo.HaveKnowledgeAbout(DiscoveryLevels.StateVectors);
                               //&& (vessel == null || !MapViewFiltering.CheckAgainstFilter(vessel));  // Add back when I do render for PatchedConicRenderer
                               //&& !(vessel.vesselType == VesselType.Debris && !orbitDriver.Renderer.isFocused);

                if (!visible)
                    continue;

                material.SetPass(0);
                
                GL.Begin(GL.LINES);

                GL.Color(orbitDriver.Renderer.orbitColor.A(0.9f));
                Orbit orbit = orbitDriver.orbit;
                double semiMinorAxis = orbit.semiMinorAxis;
                
                if (orbit.eccentricity < 1d)
                {
                    // Make sure to have a point where we are so the orbit line is actually ON the body/vessel
                    double eccentricAnomalyOrigin = orbit.eccentricAnomaly;
                    Vector3 first = ScaledSpace.LocalToScaledSpace(orbit.getPositionFromEccAnomalyWithSemiMinorAxis(eccentricAnomalyOrigin, semiMinorAxis));
                    Vector3 previous = first;
                    for (int i = 1; i < pointsCount; ++i)
                    {
                        double eccentricAnomaly = (eccentricAnomalyOrigin + i * radStep) % (2 * Mathf.PI);

                        Vector3 point = ScaledSpace.LocalToScaledSpace(orbit.getPositionFromEccAnomalyWithSemiMinorAxis(eccentricAnomaly, semiMinorAxis));

                        GL.Vertex3(previous.x, previous.y, previous.z);
                        GL.Vertex3(point.x, point.y, point.z);

                        previous = point;
                    }

                    GL.Vertex3(previous.x, previous.y, previous.z);
                    GL.Vertex3(first.x, first.y, first.z);
                }
                else
                {
                    double start = -Math.Acos(-(1d / orbit.eccentricity));
                    double end = -start;
                    double step = (end - start) / (pointsCount - 1);

                    double eccentricAnomalyOrigin = orbit.eccentricAnomaly;

                    Vector3 first = ScaledSpace.LocalToScaledSpace(orbit.getPositionFromEccAnomalyWithSemiMinorAxis(eccentricAnomalyOrigin, semiMinorAxis));

                    int i = 1;
                    Vector3 previous = first;
                    double eccentricAnomaly = eccentricAnomalyOrigin - step;
                    while (eccentricAnomaly > start)
                    {
                        Vector3 point =
                            ScaledSpace.LocalToScaledSpace(orbit.getPositionFromEccAnomalyWithSemiMinorAxis(eccentricAnomaly, semiMinorAxis));

                        GL.Vertex3(previous.x, previous.y, previous.z);
                        GL.Vertex3(point.x, point.y, point.z);
                        previous = point;

                        i++;
                        eccentricAnomaly = eccentricAnomalyOrigin - i * step;
                    } 
                    
                    i = 1;
                    previous = first;
                    eccentricAnomaly = eccentricAnomalyOrigin + step;
                    while (eccentricAnomaly < end) 
                    {
                        Vector3 point =
                            ScaledSpace.LocalToScaledSpace(orbit.getPositionFromEccAnomalyWithSemiMinorAxis(eccentricAnomaly, semiMinorAxis));

                        GL.Vertex3(previous.x, previous.y, previous.z);
                        GL.Vertex3(point.x, point.y, point.z);
                        previous = point;

                        i++;
                        eccentricAnomaly = eccentricAnomalyOrigin + i * step;
                    } 
                }
                GL.End();
            }

            GL.PopMatrix();
        }
        
        public static void print(String s)
        {
            Debug.Log("[SideView] " + s);
        }
    }

    public class FollowRotEnabler : MonoBehaviour
    {
        private FollowRot followRot;

        void Start()
        {
            followRot = GetComponent<FollowRot>();
        }

        private void Update()
        {
            if (followRot.isActiveAndEnabled && !followRot.tgt)
                followRot.enabled = false;

            if (!followRot.isActiveAndEnabled && followRot.tgt)
                followRot.enabled = true;
        }
    }











}