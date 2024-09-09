using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Configuration;
using System;
using System.Collections.Generic;

namespace OpcUa_Bz.Client.Tests
{
    public class OpcuaManagementTests
    {
        private OpcuaManagement _opcuaManagement;

        [SetUp]
        public void Setup()
        {
            _opcuaManagement = new OpcuaManagement();
        }

        [Test]
        public void CreateServerInstance_ValidConfiguration_Success()
        {
            // Arrange

            // Act
            Assert.DoesNotThrow(() => _opcuaManagement.CreateServerInstance());

            // Assert
            // No exception is thrown
        }

        [Test]
        public void CreateServerInstance_InvalidConfiguration_ThrowsException()
        {
            // Arrange
            var invalidConfig = new ApplicationConfiguration()
            {
                ApplicationName = "InvalidConfig",
                ApplicationUri = "urn:InvalidConfig",
                ApplicationType = ApplicationType.Client, // Invalid application type
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
                        SubjectName = Utils.Format(@"CN={0}, DC={1}", "InvalidConfig", System.Net.Dns.GetHostName())
                    },
                    AutoAcceptUntrustedCertificates = true,
                },
                TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
                ClientConfiguration = new ClientConfiguration { DefaultSessionTimeout = 60000 },
                TraceConfiguration = new TraceConfiguration()
            };

            // Act & Assert
            Assert.Throws<AggregateException>(() => _opcuaManagement.CreateServerInstance());
        }

        // Add more test cases for other methods if needed
    }
}
