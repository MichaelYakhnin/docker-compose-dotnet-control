using Docker.DotNet;
using Docker.DotNet.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace docker_compose_dotnet_control
{
    public class DockerService
    {
        private readonly DockerClient _client;

        public DockerService()
        {
            // Use environment variable to determine endpoint for Docker socket
            var dockerUri = Environment.OSVersion.Platform == PlatformID.Win32NT
                ? "npipe://./pipe/docker_engine"
                : "unix:///var/run/docker.sock";
            _client = new DockerClientConfiguration(new Uri(dockerUri)).CreateClient();
        }

        public async Task UpAsync(List<ComposeService> services, string folder)
        {
            foreach (var service in services)
            {

                var hostConfig = new HostConfig
                {
                    Binds = service.Volumes.Count > 0 ? service.Volumes : null
                };

                // Prefer explicit network_mode over networks list
                if (!string.IsNullOrWhiteSpace(service.NetworkMode))
                {
                    hostConfig.NetworkMode = service.NetworkMode;
                }
                else if (service.Networks.Count == 1)
                {
                    hostConfig.NetworkMode = service.Networks[0];                    
                }

                // Determine entrypoint/cmd defaults for .NET apps if not provided
                List<string>? resolvedEntrypoint = service.Entrypoint.Count > 0 ? service.Entrypoint : null;
                List<string>? resolvedCmd = service.Command.Count > 0 ? service.Command : null;
                if ((resolvedEntrypoint == null || resolvedEntrypoint.Count == 0) && (resolvedCmd == null || resolvedCmd.Count == 0) && service.IsDotNet)
                {
                    var imgLower = service.Image?.ToLowerInvariant() ?? string.Empty;
                    // Only set 'dotnet run' when using SDK images
                    if (imgLower.Contains("dotnet/sdk"))
                    {
                        resolvedEntrypoint = new List<string> { "dotnet" };
                        resolvedCmd = new List<string> { "run", "--no-restore" };
                    }
                }

                var createParams = new CreateContainerParameters
                {
                    Image = service.Image,
                        Name = string.IsNullOrWhiteSpace(service.ContainerName) ? service.Name : service.ContainerName,
                    Labels = new Dictionary<string, string>
                    {
                        { "com.docker.compose.project", folder },
                        { "com.docker.compose.service", service.Name! }
                    },
                    Env = service.Environment.Select(e => $"{e.Key}={e.Value}").ToList(),
                    Cmd = resolvedCmd,
                    Entrypoint = resolvedEntrypoint,
                    WorkingDir = string.IsNullOrWhiteSpace(service.WorkingDir) ? null : service.WorkingDir,
                    HostConfig = hostConfig,
                    NetworkingConfig = (!string.IsNullOrWhiteSpace(service.NetworkMode))
                        ? null // network_mode handles networking
                        : (service.Networks.Count > 0
                        ? new NetworkingConfig
                        {
                            EndpointsConfig = service.Networks.ToDictionary(n => n, n => new EndpointSettings())
                        }
                        : null)
                };

                // Ports: configure ExposedPorts and PortBindings
                // In host or container network_mode, Docker ignores port bindings
                var nmLower = hostConfig.NetworkMode?.ToLowerInvariant();
                var skipPorts = nmLower == "host" || (nmLower?.StartsWith("container:") ?? false);
                if (service.Ports.Count > 0 && !skipPorts)
                {
                    createParams.ExposedPorts = new Dictionary<string, EmptyStruct>();
                    hostConfig.PortBindings = new Dictionary<string, IList<PortBinding>>();

                    foreach (var p in service.Ports)
                    {
                        if (string.IsNullOrWhiteSpace(p.ContainerPort)) continue;
                        var proto = string.IsNullOrWhiteSpace(p.Protocol) ? "tcp" : p.Protocol.ToLowerInvariant();
                        var key = $"{p.ContainerPort}/{proto}";
                        if (!createParams.ExposedPorts.ContainsKey(key))
                            createParams.ExposedPorts.Add(key, default);

                        if (!hostConfig.PortBindings.ContainsKey(key))
                            hostConfig.PortBindings[key] = new List<PortBinding>();

                        // Only add a binding if host port is provided; if not, Docker can auto-assign
                        if (!string.IsNullOrWhiteSpace(p.HostPort) || !string.IsNullOrWhiteSpace(p.HostIP))
                        {
                            hostConfig.PortBindings[key].Add(new PortBinding
                            {
                                HostIP = p.HostIP,
                                HostPort = p.HostPort
                            });
                        }
                    }
                }
                if (createParams.NetworkingConfig != null)
                {
                    var networks = await _client.Networks.ListNetworksAsync();
                    if (!networks.Any(n => n.Name == service.Networks[0]))
                    {
                        await _client.Networks.CreateNetworkAsync(new NetworksCreateParameters
                        {
                            Name = hostConfig.NetworkMode,
                            Driver = "bridge"
                        });
                        Console.WriteLine($"Network {service.Networks[0]} created.");
                    }
                    else
                    {
                        Console.WriteLine($"Network {service.Networks[0]} already exists.");
                    }
                }
                var response = await _client.Containers.CreateContainerAsync(createParams);
                await _client.Containers.StartContainerAsync(response.ID, null);
            }
        }

        public async Task DownAsync(List<ComposeService> services)
        {
            var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters { All = true });
            foreach (var service in services)
            {
                foreach (var container in containers)
                {
                    if (container.Names.Contains("/" + service.Name))
                    {
                        await _client.Containers.StopContainerAsync(container.ID, new ContainerStopParameters());
                        await _client.Containers.RemoveContainerAsync(container.ID, new ContainerRemoveParameters { Force = true });
                    }
                }
            }
        }
    }
}