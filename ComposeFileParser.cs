using System.Collections.Generic;
using System.IO;
using YamlDotNet.RepresentationModel;

namespace docker_compose_dotnet_control
{
    public class ComposePort
    {
        public string? HostIP { get; set; }
        public string? HostPort { get; set; }
        public string? ContainerPort { get; set; }
        public string Protocol { get; set; } = "tcp"; // default to tcp
    }

    public class ComposeService
    {
    public string? Name { get; set; }
    public string? Image { get; set; }
    public string? ContainerName { get; set; }
    public string? WorkingDir { get; set; }
    public List<string> Volumes { get; set; } = new();
    public List<string> Command { get; set; } = new();
    public List<string> Entrypoint { get; set; } = new();
    public Dictionary<string, string> Environment { get; set; } = new();
    public List<string> Networks { get; set; } = new();
    public List<ComposePort> Ports { get; set; } = new();
    public string? NetworkMode { get; set; }
    public bool IsDotNet { get; set; }
    }

    public class ComposeFileParser
    {
    public List<ComposeService> Parse(string composeFilePath)
        {
            var servicesList = new List<ComposeService>();
            var yaml = new YamlStream();
            using (var reader = new StreamReader(composeFilePath))
                yaml.Load(reader);

            var root = (YamlMappingNode)yaml.Documents[0].RootNode;
            var services = root.Children[new YamlScalarNode("services")] as YamlMappingNode;
            if (services == null)
                return servicesList;

            foreach (var service in services.Children)
            {
                var serviceName = service.Key.ToString();
                var serviceConfig = service.Value as YamlMappingNode;
                if (serviceConfig == null)
                    continue;

                var composeService = new ComposeService { Name = serviceName };

                // Image
                if (serviceConfig.Children.ContainsKey(new YamlScalarNode("image")))
                    composeService.Image = serviceConfig.Children[new YamlScalarNode("image")].ToString();

                // Container name (optional override)
                if (serviceConfig.Children.ContainsKey(new YamlScalarNode("container_name")))
                    composeService.ContainerName = serviceConfig.Children[new YamlScalarNode("container_name")].ToString();

                // Working directory (optional)
                if (serviceConfig.Children.ContainsKey(new YamlScalarNode("working_dir")))
                    composeService.WorkingDir = serviceConfig.Children[new YamlScalarNode("working_dir")].ToString();

                // Volumes
                if (serviceConfig.Children.ContainsKey(new YamlScalarNode("volumes")))
                {
                    var volumesNode = serviceConfig.Children[new YamlScalarNode("volumes")];
                    if (volumesNode is YamlSequenceNode volumesSeq)
                    {
                        foreach (var vol in volumesSeq)
                            composeService.Volumes.Add(vol.ToString());
                    }
                }

                // Command
                if (serviceConfig.Children.ContainsKey(new YamlScalarNode("command")))
                {
                    var commandNode = serviceConfig.Children[new YamlScalarNode("command")];
                    if (commandNode is YamlSequenceNode cmdSeq)
                    {
                        foreach (var cmd in cmdSeq)
                            composeService.Command.Add(cmd.ToString());
                    }
                    else
                    {
                        composeService.Command.Add(commandNode.ToString());
                    }
                }

                // Entrypoint
                if (serviceConfig.Children.ContainsKey(new YamlScalarNode("entrypoint")))
                {
                    var epNode = serviceConfig.Children[new YamlScalarNode("entrypoint")];
                    if (epNode is YamlSequenceNode epSeq)
                    {
                        foreach (var ep in epSeq)
                            composeService.Entrypoint.Add(ep.ToString());
                    }
                    else
                    {
                        composeService.Entrypoint.Add(epNode.ToString());
                    }
                }

                // Environment
                if (serviceConfig.Children.ContainsKey(new YamlScalarNode("environment")))
                {
                    var envNode = serviceConfig.Children[new YamlScalarNode("environment")];
                    if (envNode is YamlMappingNode envMap)
                    {
                        foreach (var env in envMap.Children)
                        {
                            composeService.Environment[env.Key.ToString()] = env.Value.ToString();
                        }
                    }
                    else if (envNode is YamlSequenceNode envSeq)
                    {
                        foreach (var env in envSeq)
                        {
                            var envStr = env.ToString();
                            var parts = envStr.Split('=', 2);
                            if (parts.Length == 2)
                                composeService.Environment[parts[0]] = parts[1];
                        }
                    }
                }

                // Networks
                if (serviceConfig.Children.ContainsKey(new YamlScalarNode("networks")))
                {
                    var networksNode = serviceConfig.Children[new YamlScalarNode("networks")];
                    if (networksNode is YamlSequenceNode networksSeq)
                    {
                        foreach (var net in networksSeq)
                            composeService.Networks.Add(net.ToString());
                    }
                    else
                    {
                        composeService.Networks.Add(networksNode.ToString());
                    }
                }

                // Network mode
                if (serviceConfig.Children.ContainsKey(new YamlScalarNode("network_mode")))
                {
                    var nm = serviceConfig.Children[new YamlScalarNode("network_mode")];
                    composeService.NetworkMode = nm.ToString();
                }

                // Ports
                if (serviceConfig.Children.ContainsKey(new YamlScalarNode("ports")))
                {
                    var portsNode = serviceConfig.Children[new YamlScalarNode("ports")];
                    if (portsNode is YamlSequenceNode portsSeq)
                    {
                        foreach (var p in portsSeq)
                        {
                            if (p is YamlScalarNode scalar)
                            {
                                // Formats supported: "8080:80", "127.0.0.1:8080:80", "8080:80/tcp", "80"
                                var portStr = scalar.Value ?? string.Empty;
                                var protocol = "tcp";
                                string? containerPart = null;
                                string? hostPart = null;
                                string? hostIp = null;

                                // Split protocol
                                var protoSplit = portStr.Split('/');
                                if (protoSplit.Length == 2)
                                {
                                    portStr = protoSplit[0];
                                    var protoVal = (protoSplit[1] ?? string.Empty).Trim();
                                    protocol = string.IsNullOrWhiteSpace(protoVal) ? "tcp" : protoVal.ToLowerInvariant();
                                }

                                var parts = portStr.Split(':');
                                if (parts.Length == 3)
                                {
                                    hostIp = parts[0];
                                    hostPart = parts[1];
                                    containerPart = parts[2];
                                }
                                else if (parts.Length == 2)
                                {
                                    hostPart = parts[0];
                                    containerPart = parts[1];
                                }
                                else if (parts.Length == 1)
                                {
                                    containerPart = parts[0];
                                }

                                composeService.Ports.Add(new ComposePort
                                {
                                    HostIP = hostIp,
                                    HostPort = hostPart,
                                    ContainerPort = containerPart,
                                    Protocol = protocol
                                });
                            }
                            else if (p is YamlMappingNode map)
                            {
                                // Long syntax: target (container), published (host), protocol, host_ip
                                string? target = null;
                                string? published = null;
                                string protocol = "tcp";
                                string? hostIp = null;
                                foreach (var kv in map.Children)
                                {
                                    var key = kv.Key.ToString();
                                    var val = kv.Value.ToString();
                                    switch (key)
                                    {
                                        case "target": target = val; break;
                                        case "published": published = val; break;
                                        case "protocol":
                                            var protoVal = (val ?? string.Empty).Trim();
                                            protocol = string.IsNullOrWhiteSpace(protoVal) ? "tcp" : protoVal.ToLowerInvariant();
                                            break;
                                        case "host_ip": hostIp = val; break;
                                    }
                                }
                                composeService.Ports.Add(new ComposePort
                                {
                                    HostIP = hostIp,
                                    HostPort = published,
                                    ContainerPort = target,
                                    Protocol = protocol
                                });
                            }
                        }
                    }
                }

                // Heuristic: detect .NET apps
                var imgLower = composeService.Image?.ToLowerInvariant() ?? string.Empty;
                if (imgLower.Contains("mcr.microsoft.com/dotnet") || imgLower.Contains("microsoft/dotnet") ||
                    composeService.Command.Exists(c => (c ?? string.Empty).ToLowerInvariant().Contains("dotnet")) ||
                    composeService.Entrypoint.Exists(e => (e ?? string.Empty).ToLowerInvariant().Contains("dotnet")) ||
                    composeService.Environment.Keys.Any(k => k.StartsWith("DOTNET_")))
                {
                    composeService.IsDotNet = true;
                }

                servicesList.Add(composeService);
            }
            return servicesList;
        }
    }
}
