using Opc.Ua;
using Opc.Ua.Configuration;
using Opc.Ua.PubSub;
using Opc.Ua.Server;
using System;
using System.Collections.Generic;
using System.Timers;

// Task Manager -> AgentService -> End task

public class OpcuaManagement
{
    private UaPubSubApplication? _pubSubApplication;
    private PubSubConfigurationDataType? _pubSubConfiguration;
    private ApplicationInstance? _application;
    private OpcuaServer? _server;

    public void CreateServerInstance()
    {
        try
        {
            var config = new ApplicationConfiguration()
            {
                ApplicationName = "MyOpcua",
                ApplicationUri = "urn:MyOpcua",
                ApplicationType = ApplicationType.Server,
                ServerConfiguration = new ServerConfiguration()
                {
                    BaseAddresses = { "opc.tcp://localhost:4840" },
                    MinRequestThreadCount = 5,
                    MaxRequestThreadCount = 100,
                    MaxQueuedRequestCount = 200,
                },
                SecurityConfiguration = new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier
                    {
                        StoreType = @"Directory",
                        StorePath = @"%CommonApplicationData%\\OPC Foundation\\CertificateStores\\MachineDefault",
                        SubjectName = Utils.Format(@"CN={0}, DC={1}", "MyOpcua", Environment.MachineName)
                    },
                    AutoAcceptUntrustedCertificates = true,
                },
                TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 },
                TraceConfiguration = new TraceConfiguration()
            };

            config.Validate(ApplicationType.Server).GetAwaiter().GetResult();

            _application = new ApplicationInstance
            {
                ApplicationName = "MyOpcua",
                ApplicationType = ApplicationType.Server,
                ApplicationConfiguration = config
            };

            bool certOk = _application.CheckApplicationInstanceCertificate(false, 0).Result;
            if (!certOk)
            {
                Console.WriteLine("Certificate validation failed!");
            }

            _server = new OpcuaServer(config);
            _application.Start(_server).Wait();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error while starting OPC-UA server: " + ex.Message);
        }

        ConfigurePubSub();
        StartPubSub();
    }

    public void ConfigurePubSub()
    {
        _pubSubConfiguration = new PubSubConfigurationDataType();

        // Publisher configuration
        var publishedDataSet = new PublishedDataSetDataType
        {
            Name = "MyDataSet",
            DataSetMetaData = new DataSetMetaDataType
            {
                Name = "MyDataSet",
                Fields = new FieldMetaDataCollection {
                    new FieldMetaData { Name = "RobotPoseX", DataType = DataTypeIds.Double },
                    new FieldMetaData { Name = "RobotPoseY", DataType = DataTypeIds.Double },
                }
            }
        };

        var dataSetWriter = new DataSetWriterDataType
        {
            Name = "MyDataSetWriter",
            DataSetWriterId = 1,
            KeyFrameCount = 1
        };

        var writerGroup = new WriterGroupDataType
        {
            Name = "MyWriterGroup",
            WriterGroupId = 1,
            PublishingInterval = 1000,
            KeepAliveTime = 3000,
            DataSetWriters = new DataSetWriterDataTypeCollection { dataSetWriter }
        };

        var connectionAddress = new NetworkAddressUrlDataType
        {
            Url = "opc.udp://239.0.0.1:4840"
        };

        var publisherConnection = new PubSubConnectionDataType
        {
            Name = "MyPublisherConnection",
            PublisherId = 1,
            TransportProfileUri = Profiles.PubSubUdpUadpTransport,
            Address = new ExtensionObject(connectionAddress),
            WriterGroups = new WriterGroupDataTypeCollection { writerGroup }
        };

        _pubSubConfiguration.Connections.Add(publisherConnection);

        // Subscriber configuration
        var dataSetReader = new DataSetReaderDataType
        {
            Name = "MyDataSetReader",
            DataSetWriterId = 1,
            PublisherId = 1,
            WriterGroupId = 1,
            DataSetMetaData = publishedDataSet.DataSetMetaData
        };

        var readerGroup = new ReaderGroupDataType
        {
            Name = "MyReaderGroup",
            DataSetReaders = new DataSetReaderDataTypeCollection { dataSetReader }
        };

        var subscriberConnection = new PubSubConnectionDataType
        {
            Name = "MySubscriberConnection",
            PublisherId = 2,
            TransportProfileUri = Profiles.PubSubUdpUadpTransport,
            Address = new ExtensionObject(connectionAddress),
            ReaderGroups = new ReaderGroupDataTypeCollection { readerGroup }
        };

        _pubSubConfiguration.Connections.Add(subscriberConnection);
    }

    public void StartPubSub()
    {
        if (_pubSubConfiguration == null)
        {
            throw new InvalidOperationException("PubSub configuration is not set.");
        }

        // Tạo ứng dụng PubSub từ cấu hình đã tạo
        _pubSubApplication = UaPubSubApplication.Create(_pubSubConfiguration);

        // Bắt đầu PubSub
        _pubSubApplication.Start();
    }

    //public void StopPubSub()
    //{
    //    _pubSubApplication?.Stop();
    //}

    //public void UpdatePublishedValue(string fieldName, object value)
    //{
    //    // Implement this method to update published values
    //    // You might need to use _pubSubApplication to update the values
    //}
}

public class OpcuaServer : StandardServer
{
    private MyNodeManager? _myNodeManager;
    private ApplicationConfiguration _configuration;
    private UaPubSubApplication? _pubSubApplication;

    public OpcuaServer(ApplicationConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected override MasterNodeManager CreateMasterNodeManager(IServerInternal server, ApplicationConfiguration configuration)
    {
        List<INodeManager> nodeManagers = new List<INodeManager>();
        _myNodeManager = new MyNodeManager(server, configuration);
        nodeManagers.Add(_myNodeManager);
        return new MasterNodeManager(server, configuration, null, nodeManagers.ToArray());
    }

    protected override ServerProperties LoadServerProperties()
    {
        return new ServerProperties
        {
            ManufacturerName = "OPC Foundation",
            ProductName = "MyOpcuaServer",
            ProductUri = "http://amr150.rcs_server/",
            SoftwareVersion = Utils.GetAssemblySoftwareVersion(),
            BuildNumber = Utils.GetAssemblyBuildNumber(),
            BuildDate = Utils.GetAssemblyTimestamp()
        };
    }

    public new IServerInternal ServerInternal => base.ServerInternal;

    protected override void OnServerStarting(ApplicationConfiguration configuration)
    {
        base.OnServerStarting(_configuration);

        _pubSubApplication = UaPubSubApplication.Create(CreatePubSubConfiguration());
        _pubSubApplication.Start();
    }
    private PubSubConfigurationDataType CreatePubSubConfiguration()
    {
        var config = new PubSubConfigurationDataType();

        var connection = new PubSubConnectionDataType
        {
            Name = "MyConnection",
            TransportProfileUri = Profiles.PubSubMqttJsonTransport,
            Address = new ExtensionObject(new NetworkAddressUrlDataType
            {
                Url = "mqtt://localhost:1883"
            }),
            PublisherId = 1,
            WriterGroups = new WriterGroupDataTypeCollection()
        };

        var writerGroup = new WriterGroupDataType
        {
            Name = "WriterGroup",
            WriterGroupId = 1,
            PublishingInterval = 1000,
            KeepAliveTime = 6000,
            DataSetWriters = new DataSetWriterDataTypeCollection()
        };

        var dataSetWriter = new DataSetWriterDataType
        {
            Name = "DataSetWriter",
            DataSetWriterId = 1,
            KeyFrameCount = 1
        };

        writerGroup.DataSetWriters.Add(dataSetWriter);
        connection.WriterGroups.Add(writerGroup);
        config.Connections.Add(connection);

        return config;
    }
}

public class MyNodeManager : CustomNodeManager2, INodeManager, INodeIdFactory, IDisposable
{
    public class Node
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Yaw { get; set; }
        public double Vmax { get; set; }
        public double Accuracy { get; set; }

        public Node(double x, double y, double yaw, double vmax, double accuracy)
        {
            X = x;
            Y = y;
            Yaw = yaw;
            Vmax = vmax;
            Accuracy = accuracy;
        }
    }

    private List<Node> nodes = new List<Node>();

    public void AddNode(double x, double y, double yaw, double vmax, double accuracy)
    {
        nodes.Add(new Node(x, y, yaw, vmax, accuracy));
    }

    // timer để định kỳ cập nhật giá trị của các biến
    private System.Timers.Timer _updateTimer;

    // Định nghĩa các mảng đối số
    private static readonly Argument[] EmptyArguments = new Argument[] { };

    private static readonly Argument[] MapName = new[]
    {
            new Argument() { Name = "map_name", DataType = DataTypeIds.String, ValueRank = ValueRanks.Scalar }
    };

    private static readonly Argument[] StatusArguments = new[]
    {
            new Argument { Name = "status", DataType = DataTypeIds.Byte, ValueRank = ValueRanks.Scalar },
            new Argument { Name = "message", DataType = DataTypeIds.String, ValueRank = ValueRanks.Scalar }
    };
    private static readonly Argument[] MoveArguments = new[]
{
            new Argument { Name = "x", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar },
            new Argument { Name = "y", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar },
            new Argument { Name = "yaw", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar },
            new Argument { Name = "vmax", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar },
            new Argument { Name = "accuracy", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar }
    };

    private List<Argument[]> NodeMArguments = new List<Argument[]>();
    public void AddMoveArguments()
    {
        NodeMArguments.Add(MoveArguments);
    }

    // Phương thức để lấy NodeArguments dưới dạng mảng
    public Argument[][] GetNodeArguments()
    {
        return NodeMArguments.ToArray();
    }

    #region Declarations
    private BaseObjectState session;

    private PropertyState<double> robotPoseX;
    private PropertyState<double> robotPoseY;
    private PropertyState<double> robotPoseYaw;
    private PropertyState<string> slamStateVariable;
    private PropertyState<string> slamStateDetailVariable;
    private PropertyState<string> currentActiveMap;
    private PropertyState<double> localizationQuality;
    private PropertyState<double> batteryState;
    private PropertyState<double> batterySoC;
    private PropertyState<double> batteryCycles;
    private PropertyState<double> batteryVoltage;
    private PropertyState<double> batteryCurrent;
    private PropertyState<double> linearVelocity;
    private PropertyState<double> angularVelocity;
    private PropertyState<double[]> currentPath;
    private PropertyState<double[]> laserScanData;
    private Random random = new Random();

    private MethodState startMappingMethod;
    private MethodState stopMappingMethod;
    private MethodState startLocalizationMethod;
    private MethodState stopLocalizationMethod;
    private MethodState activateMapMethod;
    private MethodState setInitialPoseMethod;
    private MethodState resetSlamErrorMethod;
    private MethodState stopCalibrateMethod;

    private MethodState moveToNodeMethod;
    private MethodState dockToShelfMethod;
    private MethodState dropTheShelfMethod;
    private MethodState rotateMethod;
    private MethodState moveStraightMethod;
    private MethodState dockToChargerMethod;
    private MethodState undockFromChargerMethod;
    private MethodState cancelNavigationMethod;
    private MethodState pauseMethod;
    private MethodState resumeMethod;

    private string state_detail;
    public PropertyState<string> SlamStateDetailVariable { get; set; }

    #endregion
    public MyNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        : base(server, configuration, "http://amr150.rcs_server/")
    {
        session = new BaseObjectState(null);
        robotPoseX = new PropertyState<double>(session);
        robotPoseY = new PropertyState<double>(session);
        robotPoseYaw = new PropertyState<double>(session);
        slamStateVariable = new PropertyState<string>(session);
        slamStateDetailVariable = new PropertyState<string>(session) { Value = string.Empty };
        SlamStateDetailVariable = slamStateDetailVariable;
        currentActiveMap = new PropertyState<string>(session);
        localizationQuality = new PropertyState<double>(session);
        laserScanData = new PropertyState<double[]>(session);
        startMappingMethod = new MethodState(session);
        stopMappingMethod = new MethodState(session);
        startLocalizationMethod = new MethodState(session);
        stopLocalizationMethod = new MethodState(session);
        activateMapMethod = new MethodState(session);
        setInitialPoseMethod = new MethodState(session);
        resetSlamErrorMethod = new MethodState(session);
        stopCalibrateMethod = new MethodState(session);
        moveToNodeMethod = new MethodState(session);
        dockToShelfMethod = new MethodState(session);
        dropTheShelfMethod = new MethodState(session);
        rotateMethod = new MethodState(session);
        moveStraightMethod = new MethodState(session);
        dockToChargerMethod = new MethodState(session);
        undockFromChargerMethod = new MethodState(session);
        cancelNavigationMethod = new MethodState(session);
        pauseMethod = new MethodState(session);
        resumeMethod = new MethodState(session);
        batteryState = new PropertyState<double>(session);
        batterySoC = new PropertyState<double>(session);
        batteryCycles = new PropertyState<double>(session);
        batteryVoltage = new PropertyState<double>(session);
        batteryCurrent = new PropertyState<double>(session);
        linearVelocity = new PropertyState<double>(session);
        angularVelocity = new PropertyState<double>(session);
        currentPath = new PropertyState<double[]>(session);


        state_detail = string.Empty;
        _updateTimer = new System.Timers.Timer(1000);
        NewMethod();
        _updateTimer.Start();
    }

    private void NewMethod()
    {
        _updateTimer.Elapsed += UpdateVariables!;
    }

    protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
    {

        NodeStateCollection predefinedNodes = new NodeStateCollection();

        session = CreateObject(null!, "SESSION", "SESSION");
        predefinedNodes.Add(session);

        robotPoseX = CreateDataVariable<double>(session, "RobotPoseX", "Robot PoseX", DataTypeIds.Double, ValueRanks.Scalar);
        robotPoseX.Value = 0.0;
        robotPoseX.TypeDefinitionId = VariableTypeIds.PropertyType;
        robotPoseX.ReferenceTypeId = ReferenceTypes.HasProperty;
        predefinedNodes.Add(robotPoseX);

        robotPoseY = CreateDataVariable<double>(session, "RobotPoseY", "Robot PoseY", DataTypeIds.Double, ValueRanks.Scalar);
        robotPoseY.Value = 0.0;
        robotPoseY.TypeDefinitionId = VariableTypeIds.PropertyType;
        robotPoseY.ReferenceTypeId = ReferenceTypes.HasProperty;
        predefinedNodes.Add(robotPoseY);

        robotPoseYaw = CreateDataVariable<double>(session, "RobotPoseYaw", "Robot PoseYaw", DataTypeIds.Double, ValueRanks.Scalar);
        robotPoseYaw.Value = 0.0;
        robotPoseYaw.TypeDefinitionId = VariableTypeIds.PropertyType;
        robotPoseYaw.ReferenceTypeId = ReferenceTypes.HasProperty;
        predefinedNodes.Add(robotPoseYaw);

        slamStateVariable = CreateDataVariable<string>(session, "SlamState", "Slam State", DataTypeIds.String, ValueRanks.Scalar);
        slamStateVariable.Value = "Ready1";
        slamStateVariable.TypeDefinitionId = VariableTypeIds.PropertyType;
        slamStateVariable.ReferenceTypeId = ReferenceTypes.HasProperty;
        predefinedNodes.Add(slamStateVariable);

        state_detail = "No details available 1";
        SlamStateDetailVariable = CreateDataVariable<string>(session, "SlamStateDetail", "Slam State Detail", DataTypeIds.String, ValueRanks.Scalar);
        SlamStateDetailVariable.Value = state_detail;
        SlamStateDetailVariable.TypeDefinitionId = VariableTypeIds.PropertyType;
        SlamStateDetailVariable.ReferenceTypeId = ReferenceTypes.HasProperty;
        predefinedNodes.Add(SlamStateDetailVariable);

        currentActiveMap = CreateDataVariable<string>(session, "CurrentActiveMap", "Current Active Map", DataTypeIds.String, ValueRanks.Scalar);
        currentActiveMap.Value = "No active map 1";
        currentActiveMap.TypeDefinitionId = VariableTypeIds.PropertyType;
        currentActiveMap.ReferenceTypeId = ReferenceTypes.HasProperty;
        predefinedNodes.Add(currentActiveMap);

        localizationQuality = CreateDataVariable<double>(session, "LocalizationQuality", "Localization Quality", DataTypeIds.Byte, ValueRanks.Scalar);
        localizationQuality.Value = 0;
        localizationQuality.TypeDefinitionId = VariableTypeIds.PropertyType;
        localizationQuality.ReferenceTypeId = ReferenceTypes.HasProperty;
        predefinedNodes.Add(localizationQuality);

        startMappingMethod = CreateMethod(session, "StartMapping", "Start Mapping", StartMappingMethod, EmptyArguments, StatusArguments);
        predefinedNodes.Add(startMappingMethod);

        stopMappingMethod = CreateMethod(session, "StopMapping", "Stop Mapping", StopMappingMethod, MapName, StatusArguments);
        predefinedNodes.Add(stopMappingMethod);

        startLocalizationMethod = CreateMethod(session, "StartLocalization", "Start Localization", StartLocalizationMethod, EmptyArguments, StatusArguments);
        predefinedNodes.Add(startLocalizationMethod);

        stopLocalizationMethod = CreateMethod(session, "StopLocalization", "Stop Localization", StopLocalizationMethod, EmptyArguments, StatusArguments);
        predefinedNodes.Add(stopLocalizationMethod);

        activateMapMethod = CreateMethod(session, "ActivateMap", "Activate Map", ActivateMapMethod, MapName, StatusArguments);
        predefinedNodes.Add(activateMapMethod);

        setInitialPoseMethod = CreateMethod(session, "SetInitialPose", "Set Initial Pose", SetInitialPoseMethod,
        new Argument[] {
                    new Argument { Name = "x", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar },
                    new Argument { Name = "y", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar },
                    new Argument { Name = "yaw", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar }
        },
        StatusArguments);
        predefinedNodes.Add(setInitialPoseMethod);

        resetSlamErrorMethod = CreateMethod(session, "ResetSlamError", "Reset Slam Error", ResetSlamErrorMethod, EmptyArguments, StatusArguments);
        predefinedNodes.Add(resetSlamErrorMethod);

        stopCalibrateMethod = CreateMethod(session, "StopCalibrate", "Stop Calibrate", StopCalibrateMethod, EmptyArguments, StatusArguments);
        predefinedNodes.Add(stopCalibrateMethod);

        //laserScanData.Value = new double[,] { };
        laserScanData = CreateDataVariable<double[]>(session, "LaserScan", "Laser Scan Data", DataTypeIds.Double, ValueRanks.OneDimension);
        //laserScanData.Value = GenerateSamp();
        laserScanData.TypeDefinitionId = VariableTypeIds.PropertyType;
        laserScanData.ReferenceTypeId = ReferenceTypes.HasProperty;
        predefinedNodes.Add(laserScanData);

        // Thêm một số Node mẫu
        AddNode(0.0, 0.0, 0.0, 0.0, 0.0);

        moveToNodeMethod = CreateDynamicMethod(session, "MoveToNode", "Move to Node", MoveToNodeMethod);
        predefinedNodes.Add(moveToNodeMethod);

        dockToShelfMethod = CreateDynamicMethod(session, "DockToShelf", "Dock to Shelf", DockToShelfMethod);
        predefinedNodes.Add(dockToShelfMethod);

        dropTheShelfMethod = CreateDynamicMethod(session, "DropTheShelf", "Drop the Shelf", DropTheShelfMethod);
        predefinedNodes.Add(dropTheShelfMethod);

        rotateMethod = CreateMethod(session, "Rotate", "Rotate", RotateMethod,
            new Argument[] {
                    new Argument() { Name = "alpha", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar }
            },
            StatusArguments
        );
        predefinedNodes.Add(rotateMethod);

        moveStraightMethod = CreateMethod(session, "MoveStraight", "Move Straight", MoveStraightMethod,
            new Argument[] {
                    new Argument() { Name = "x", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar },
                    new Argument() { Name = "y", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar },
            },
            StatusArguments
        );
        predefinedNodes.Add(moveStraightMethod);

        dockToChargerMethod = CreateDynamicMethod(session, "DockToCharger", "Dock to Charger", DockToChargerMethod);
        predefinedNodes.Add(dockToChargerMethod);

        undockFromChargerMethod = CreateMethod(session, "UndockFromCharger", "Undock from Charger", UndockFromChargerMethod, EmptyArguments, StatusArguments);
        predefinedNodes.Add(undockFromChargerMethod);

        cancelNavigationMethod = CreateMethod(session, "CancelNavigation", "Cancel Navigation", CancelNavigationMethod,
            new Argument[] {
                    new Argument() { Name = "softStop", DataType = DataTypeIds.Boolean, ValueRank = ValueRanks.Scalar }
            },
            StatusArguments
        );
        predefinedNodes.Add(cancelNavigationMethod);

        pauseMethod = CreateMethod(session, "Pause", "Pause", PauseMethod, EmptyArguments, StatusArguments);
        predefinedNodes.Add(pauseMethod);

        resumeMethod = CreateMethod(session, "Resume", "Resume", ResumeMethod, EmptyArguments, StatusArguments);
        predefinedNodes.Add(resumeMethod);

        currentPath = CreateDataVariable<double[]>(session, "CurrentPath", "Current Path", DataTypeIds.Double, ValueRanks.OneDimension);
        currentPath.Value = new double[] { };
        //currentPath.Value = GenerateSampcurrentPath();
        currentPath.TypeDefinitionId = VariableTypeIds.PropertyType;
        currentPath.ReferenceTypeId = ReferenceTypes.HasProperty;
        predefinedNodes.Add(currentPath);

        batteryState = CreateDataVariable<double>(session, "BatteryState", "Battery State", DataTypeIds.Byte, ValueRanks.Scalar);
        batteryState.Value = 0.0;
        batteryState.TypeDefinitionId = VariableTypeIds.PropertyType;
        batteryState.ReferenceTypeId = ReferenceTypes.HasProperty;
        predefinedNodes.Add(batteryState);

        batterySoC = CreateDataVariable<double>(session, "BatterySoC", "Battery SoC", DataTypeIds.Double, ValueRanks.Scalar);
        batterySoC.Value = 0.0;
        batterySoC.TypeDefinitionId = VariableTypeIds.PropertyType;
        batterySoC.ReferenceTypeId = ReferenceTypes.HasProperty;
        predefinedNodes.Add(batterySoC);

        batteryCycles = CreateDataVariable<double>(session, "BatteryCycles", "Battery Cycles", DataTypeIds.UInt16, ValueRanks.Scalar);
        batteryCycles.Value = 0.0;
        batteryCycles.TypeDefinitionId = VariableTypeIds.PropertyType;
        batteryCycles.ReferenceTypeId = ReferenceTypes.HasProperty;
        predefinedNodes.Add(batteryCycles);

        batteryVoltage = CreateDataVariable<double>(session, "BatteryVoltage", "Battery Voltage", DataTypeIds.Double, ValueRanks.Scalar);
        batteryVoltage.Value = 0.0;
        batteryVoltage.TypeDefinitionId = VariableTypeIds.PropertyType;
        batteryVoltage.ReferenceTypeId = ReferenceTypes.HasProperty;
        predefinedNodes.Add(batteryVoltage);

        batteryCurrent = CreateDataVariable<double>(session, "BatteryCurrent", "Battery Current", DataTypeIds.Double, ValueRanks.Scalar);
        batteryCurrent.Value = 0.0;
        batteryCurrent.TypeDefinitionId = VariableTypeIds.PropertyType;
        batteryCurrent.ReferenceTypeId = ReferenceTypes.HasProperty;
        predefinedNodes.Add(batteryCurrent);

        linearVelocity = CreateDataVariable<double>(session, "LinearVelocity", "Current Linear Velocity", DataTypeIds.Double, ValueRanks.Scalar);
        linearVelocity.Value = 0.0;
        linearVelocity.TypeDefinitionId = VariableTypeIds.PropertyType;
        linearVelocity.ReferenceTypeId = ReferenceTypes.HasProperty;
        predefinedNodes.Add(linearVelocity);

        angularVelocity = CreateDataVariable<double>(session, "AngularVelocity", "Current Angular Velocity", DataTypeIds.Double, ValueRanks.Scalar);
        angularVelocity.Value = 0.0;
        angularVelocity.TypeDefinitionId = VariableTypeIds.PropertyType;
        angularVelocity.ReferenceTypeId = ReferenceTypes.HasProperty;
        predefinedNodes.Add(angularVelocity);

        MethodState updateDataMethod = CreateMethod(session, "UpdateData", "Update Data from Client", UpdateDataMethod,
        new Argument[]
        {
            new Argument { Name = "newX", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar },
            new Argument { Name = "newY", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar },
            new Argument { Name = "newYaw", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar },
            new Argument { Name = "newSlamState", DataType = DataTypeIds.String, ValueRank = ValueRanks.Scalar },
            new Argument { Name = "newSlamStateDetail", DataType = DataTypeIds.String, ValueRank = ValueRanks.Scalar },
            new Argument { Name = "newCurrentActiveMap", DataType = DataTypeIds.String, ValueRank = ValueRanks.Scalar },
            new Argument { Name = "newLocalizationQuality", DataType = DataTypeIds.Byte, ValueRank = ValueRanks.Scalar },
            new Argument { Name = "newLaserScanData", DataType = DataTypeIds.Double, ValueRank = ValueRanks.TwoDimensions },
            new Argument { Name = "newBatteryState", DataType = DataTypeIds.Byte, ValueRank = ValueRanks.Scalar },
            new Argument { Name = "newBatterySoC", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar },
            new Argument { Name = "newBatteryCycles", DataType = DataTypeIds.UInt16, ValueRank = ValueRanks.Scalar },
            new Argument { Name = "newBatteryVoltage", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar },
            new Argument { Name = "newBatteryCurrent", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar },
            new Argument { Name = "newLinearVelocity", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar },
            new Argument { Name = "newAngularVelocity", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar },
            new Argument { Name = "newCurrentPath", DataType = DataTypeIds.Double, ValueRank = ValueRanks.OneDimension }
        },
        StatusArguments);
        predefinedNodes.Add(updateDataMethod);

        // Hiển thị các node cho PubSub
        ExposeNodeForPubSub(robotPoseX);
        ExposeNodeForPubSub(robotPoseY);
        ExposeNodeForPubSub(robotPoseYaw);
        ExposeNodeForPubSub(slamStateVariable);
        ExposeNodeForPubSub(SlamStateDetailVariable);
        ExposeNodeForPubSub(currentActiveMap);
        ExposeNodeForPubSub(localizationQuality);
        ExposeNodeForPubSub(laserScanData);
        ExposeNodeForPubSub(batteryState);
        ExposeNodeForPubSub(batterySoC);
        ExposeNodeForPubSub(batteryCycles);
        ExposeNodeForPubSub(batteryVoltage);
        ExposeNodeForPubSub(batteryCurrent);
        ExposeNodeForPubSub(linearVelocity);
        ExposeNodeForPubSub(angularVelocity);
        ExposeNodeForPubSub(currentPath);

        return predefinedNodes;
    }
    #region PubSub

    private void ExposeNodeForPubSub(BaseVariableState variable)
    {
        variable.OnSimpleWriteValue = OnWriteValue;
        variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
        variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
    }

    private ServiceResult OnWriteValue(ISystemContext context, NodeState node, ref object value)
    {
        var variable = node as BaseVariableState;
        if (variable != null)
        {
            variable.Value = value;
            variable.Timestamp = DateTime.UtcNow;
            variable.ClearChangeMasks(context, false);
        }
        return ServiceResult.Good;
    }
    private ServiceResult UpdateDataMethod(
           ISystemContext context,
           MethodState method,
           IList<object> inputArguments,
           IList<object> outputArguments)
    {
        try
        {
            // Xử lý dữ liệu đầu vào
            double newX = Convert.ToDouble(inputArguments[0]);
            double newY = Convert.ToDouble(inputArguments[1]);
            double newYaw = Convert.ToDouble(inputArguments[2]);
            string newSlamState = inputArguments[3]?.ToString() ?? string.Empty;
            string newSlamStateDetail = inputArguments[4]?.ToString() ?? string.Empty;
            string newActiveMap = inputArguments[5]?.ToString() ?? string.Empty;
            double newLocalizationQuality = Convert.ToDouble(inputArguments[6]);
            double[] newLaserScanData = (double[])inputArguments[7];
            ushort newBatteryState = Convert.ToUInt16(inputArguments[8]);
            double newBatterySoC = Convert.ToDouble(inputArguments[9]);
            ushort newBatteryCycles = Convert.ToUInt16(inputArguments[10]);
            double newBatteryVoltage = Convert.ToDouble(inputArguments[11]);
            double newBatteryCurrent = Convert.ToDouble(inputArguments[12]);
            double newLinearVelocity = Convert.ToDouble(inputArguments[13]);
            double newAngularVelocity = Convert.ToDouble(inputArguments[14]);
            double[] newCurrentPath = (double[])inputArguments[15];

            // Cập nhật các biến
            UpdateVariable(robotPoseX, newX);
            UpdateVariable(robotPoseY, newY);
            UpdateVariable(robotPoseYaw, newYaw);
            UpdateVariable(slamStateVariable, newSlamState);
            UpdateVariable(SlamStateDetailVariable, newSlamStateDetail);
            UpdateVariable(currentActiveMap, newActiveMap);
            UpdateVariable(localizationQuality, newLocalizationQuality);
            UpdateVariable(laserScanData, newLaserScanData);
            UpdateVariable(batteryState, newBatteryState);
            UpdateVariable(batterySoC, newBatterySoC);
            UpdateVariable(batteryCycles, newBatteryCycles);
            UpdateVariable(batteryVoltage, newBatteryVoltage);
            UpdateVariable(batteryCurrent, newBatteryCurrent);
            UpdateVariable(linearVelocity, newLinearVelocity);
            UpdateVariable(angularVelocity, newAngularVelocity);
            UpdateVariable(currentPath, newCurrentPath);
            outputArguments[0] = (byte)0; // status
            outputArguments[1] = "Data updated successfully"; // message

            return ServiceResult.Good;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in UpdateDataMethod: {ex.Message}");
            outputArguments[0] = (byte)1; // error status
            outputArguments[1] = $"Error updating data: {ex.Message}";
            return ServiceResult.Create(StatusCodes.Bad, "Error updating data", ex.Message);
        }
    }

    private void UpdateVariable<T>(PropertyState<T> variable, T newValue)
    {
        variable.Value = newValue;
        variable.Timestamp = DateTime.UtcNow;
        variable.ClearChangeMasks(SystemContext, true);
    }
    private void UpdateVariables(object sender, ElapsedEventArgs e)
    {
    }

    #endregion


    #region Create Address Space

    public override void CreateAddressSpace(
        IDictionary<NodeId,
        IList<IReference>> externalReferences)
    {
        lock (Lock)
        {
            LoadPredefinedNodes(SystemContext, externalReferences);

            IList<IReference> references = null!;
            if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out references!))
            {
                externalReferences[ObjectIds.ObjectsFolder] = references = new List<IReference>();
            }

            references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, session.NodeId));
            session.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);

            AddPredefinedNode(SystemContext, session);
        }
    }

    private BaseObjectState CreateObject(
        NodeState parent,
        string name,
        string description)
    {
        BaseObjectState node = new BaseObjectState(parent)
        {
            SymbolicName = name,
            ReferenceTypeId = ReferenceTypes.Organizes,
            TypeDefinitionId = ObjectTypeIds.BaseObjectType,
            NodeId = new NodeId(name, NamespaceIndex),
            BrowseName = new QualifiedName(name, NamespaceIndex),
            DisplayName = new LocalizedText(description),
            Description = new LocalizedText(description),
            WriteMask = AttributeWriteMask.None,
            UserWriteMask = AttributeWriteMask.None,
            EventNotifier = EventNotifiers.None
        };

        if (parent != null)
        {
            parent.AddChild(node);
        }

        return node;
    }

    private PropertyState<T> CreateDataVariable<T>(
        NodeState parent,
        string name,
        string description,
        NodeId dataType,
        int valueRank)
    {
        PropertyState<T> variable = new PropertyState<T>(parent)
        {
            SymbolicName = name,
            NodeId = new NodeId(name, NamespaceIndex),
            BrowseName = new QualifiedName(name, NamespaceIndex),
            DisplayName = new LocalizedText(description),
            Description = new LocalizedText(description),
            WriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description,
            UserWriteMask = AttributeWriteMask.DisplayName | AttributeWriteMask.Description,
            DataType = dataType,
            ValueRank = valueRank,
            AccessLevel = AccessLevels.CurrentReadOrWrite,
            UserAccessLevel = AccessLevels.CurrentReadOrWrite,
            Historizing = false,
            Value = default(T)!,
            StatusCode = StatusCodes.Good,
            Timestamp = DateTime.UtcNow,
            MinimumSamplingInterval = 100
        };

        if (parent != null)
        {
            parent.AddChild(variable);
        }

        return variable;
    }
    private MethodState CreateDynamicMethod(
        NodeState parent,
        string name,
        string description,
        GenericMethodCalledEventHandler handler)
    {
        MethodState method = new MethodState(parent)
        {
            SymbolicName = name,
            ReferenceTypeId = ReferenceTypes.HasComponent,
            NodeId = new NodeId(name, NamespaceIndex),
            BrowseName = new QualifiedName(name, NamespaceIndex),
            DisplayName = new LocalizedText(description),
            Description = new LocalizedText(description),
            WriteMask = AttributeWriteMask.None,
            UserWriteMask = AttributeWriteMask.None,
            Executable = true,
            UserExecutable = true
        };

        List<Argument> inputArguments = new List<Argument>();
        for (int i = 0; i < nodes.Count; i++)
        {
            inputArguments.Add(new Argument { Name = $"x{i}", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar });
            inputArguments.Add(new Argument { Name = $"y{i}", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar });
            inputArguments.Add(new Argument { Name = $"yaw{i}", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar });
            inputArguments.Add(new Argument { Name = $"vmax{i}", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar });
            inputArguments.Add(new Argument { Name = $"accuracy{i}", DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar });
        }

        method.InputArguments = new PropertyState<Argument[]>(method)
        {
            NodeId = new NodeId(method.NodeId.Identifier + "-InArgs", NamespaceIndex),
            BrowseName = BrowseNames.InputArguments,
            DisplayName = new LocalizedText("Input Arguments"),
            TypeDefinitionId = VariableTypeIds.PropertyType,
            ReferenceTypeId = ReferenceTypes.HasProperty,
            DataType = DataTypeIds.Argument,
            ValueRank = ValueRanks.OneDimension,
            Value = inputArguments.ToArray()
        };

        method.OutputArguments = new PropertyState<Argument[]>(method)
        {
            NodeId = new NodeId(method.NodeId.Identifier + "-OutArgs", NamespaceIndex),
            BrowseName = BrowseNames.OutputArguments,
            DisplayName = new LocalizedText("Output Arguments"),
            TypeDefinitionId = VariableTypeIds.PropertyType,
            ReferenceTypeId = ReferenceTypes.HasProperty,
            DataType = DataTypeIds.Argument,
            ValueRank = ValueRanks.OneDimension,
            Value = StatusArguments
        };

        method.OnCallMethod = handler;

        if (parent != null)
        {
            parent.AddChild(method);
        }

        return method;
    }

    private MethodState CreateMethod(
           NodeState parent,
           string name,
           string description,
           GenericMethodCalledEventHandler handler,
           object inputArguments,
           object outputArguments)
    {
        MethodState method = new MethodState(parent)
        {
            SymbolicName = name,
            ReferenceTypeId = ReferenceTypes.HasComponent,
            NodeId = new NodeId(name, NamespaceIndex),
            BrowseName = new QualifiedName(name, NamespaceIndex),
            DisplayName = new LocalizedText(description),
            Description = new LocalizedText(description),
            WriteMask = AttributeWriteMask.None,
            UserWriteMask = AttributeWriteMask.None,
            Executable = true,
            UserExecutable = true
        };

        Argument[] flattenedInputArgs = FlattenArguments(inputArguments);
        Argument[] flattenedOutputArgs = FlattenArguments(outputArguments);

        method.InputArguments = new PropertyState<Argument[]>(method)
        {
            NodeId = new NodeId(method.NodeId.Identifier + "-InArgs", NamespaceIndex),
            BrowseName = BrowseNames.InputArguments,
            DisplayName = new LocalizedText("Input Arguments"),
            TypeDefinitionId = VariableTypeIds.PropertyType,
            ReferenceTypeId = ReferenceTypes.HasProperty,
            DataType = DataTypeIds.Argument,
            ValueRank = ValueRanks.OneDimension,
            Value = flattenedInputArgs
        };

        method.OutputArguments = new PropertyState<Argument[]>(method)
        {
            NodeId = new NodeId(method.NodeId.Identifier + "-OutArgs", NamespaceIndex),
            BrowseName = BrowseNames.OutputArguments,
            DisplayName = new LocalizedText("Output Arguments"),
            TypeDefinitionId = VariableTypeIds.PropertyType,
            ReferenceTypeId = ReferenceTypes.HasProperty,
            DataType = DataTypeIds.Argument,
            ValueRank = ValueRanks.OneDimension,
            Value = flattenedOutputArgs
        };

        method.OnCallMethod = handler;

        if (parent != null)
        {
            parent.AddChild(method);
        }

        return method;
    }

    private Argument[] FlattenArguments(object arguments)
    {
        if (arguments is Argument[] args1D)
        {
            return args1D;
        }
        else if (arguments is Argument[][] args2D)
        {
            return args2D.SelectMany(args => args).ToArray();
        }
        else if (arguments is IEnumerable<Argument[]> argsList)
        {
            return argsList.SelectMany(args => args).ToArray();
        }
        else
        {
            throw new ArgumentException("Unsupported argument type", nameof(arguments));
        }
    }
    #endregion

    #region Service Methods

    private ServiceResult StartMappingMethod(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments)
    {
        return ServiceResult.Good;
    }

    private ServiceResult StopMappingMethod(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments)
    {
        return ServiceResult.Good;
    }

    private ServiceResult StartLocalizationMethod(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments)
    {
        return ServiceResult.Good;
    }

    private ServiceResult StopLocalizationMethod(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments)
    {
        return ServiceResult.Good;
    }

    private ServiceResult ActivateMapMethod(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments)
    {
        return ServiceResult.Good;
    }

    private ServiceResult SetInitialPoseMethod(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments)
    {
        return ServiceResult.Good;
    }

    private ServiceResult ResetSlamErrorMethod(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments)
    {
        return ServiceResult.Good;
    }

    private ServiceResult StopCalibrateMethod(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments)
    {
        return ServiceResult.Good;
    }

    private ServiceResult MoveToNodeMethod(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            int baseIndex = i * 5;
            double x = (double)inputArguments[baseIndex];
            double y = (double)inputArguments[baseIndex + 1];
            double yaw = (double)inputArguments[baseIndex + 2];
            double vmax = (double)inputArguments[baseIndex + 3];
            double accuracy = (double)inputArguments[baseIndex + 4];

            Console.WriteLine($"Node {i}: x={x}, y={y}, yaw={yaw}, vmax={vmax}, accuracy={accuracy}");
        }

        outputArguments[0] = (byte)0; // status
        outputArguments[1] = "Command received"; // message

        return ServiceResult.Good;
    }

    private ServiceResult DockToShelfMethod(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            int baseIndex = i * 5;
            double x = (double)inputArguments[baseIndex];
            double y = (double)inputArguments[baseIndex + 1];
            double yaw = (double)inputArguments[baseIndex + 2];
            double vmax = (double)inputArguments[baseIndex + 3];
            double accuracy = (double)inputArguments[baseIndex + 4];

            Console.WriteLine($"Received MoveToNode command for node {i}: x={x}, y={y}, yaw={yaw}, vmax={vmax}, accuracy={accuracy}");
        }

        outputArguments[0] = (byte)0; // status
        outputArguments[1] = "Command received"; // message
        return ServiceResult.Good;
    }

    private ServiceResult DropTheShelfMethod(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            int baseIndex = i * 5;
            double x = (double)inputArguments[baseIndex];
            double y = (double)inputArguments[baseIndex + 1];
            double yaw = (double)inputArguments[baseIndex + 2];
            double vmax = (double)inputArguments[baseIndex + 3];
            double accuracy = (double)inputArguments[baseIndex + 4];

            Console.WriteLine($"Received MoveToNode command for node {i}: x={x}, y={y}, yaw={yaw}, vmax={vmax}, accuracy={accuracy}");
        }

        outputArguments[0] = (byte)0; // status
        outputArguments[1] = "Command received"; // message
        return ServiceResult.Good;
    }

    private ServiceResult RotateMethod(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments)
    {
        return ServiceResult.Good;
    }

    private ServiceResult MoveStraightMethod(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments)
    {
        return ServiceResult.Good;
    }

    private ServiceResult DockToChargerMethod(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments)
    {
        for (int i = 0; i < nodes.Count; i++)
        {
            int baseIndex = i * 5;
            double x = (double)inputArguments[baseIndex];
            double y = (double)inputArguments[baseIndex + 1];
            double yaw = (double)inputArguments[baseIndex + 2];
            double vmax = (double)inputArguments[baseIndex + 3];
            double accuracy = (double)inputArguments[baseIndex + 4];

            Console.WriteLine($"Received MoveToNode command for node {i}: x={x}, y={y}, yaw={yaw}, vmax={vmax}, accuracy={accuracy}");
        }

        outputArguments[0] = (byte)0;
        outputArguments[1] = "";
        return ServiceResult.Good;
    }

    private ServiceResult UndockFromChargerMethod(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments)
    {
        return ServiceResult.Good;
    }

    private ServiceResult CancelNavigationMethod(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments)
    {
        return ServiceResult.Good;
    }

    private ServiceResult PauseMethod(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments)
    {
        return ServiceResult.Good;
    }

    private ServiceResult ResumeMethod(
        ISystemContext context,
        MethodState method,
        IList<object> inputArguments,
        IList<object> outputArguments)
    {
        return ServiceResult.Good;
    }
    #endregion

}
