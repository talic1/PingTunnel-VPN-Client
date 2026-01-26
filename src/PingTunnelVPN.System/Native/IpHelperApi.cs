using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace PingTunnelVPN.Platform.Native;

/// <summary>
/// P/Invoke declarations for Windows IP Helper API.
/// </summary>
internal static class IpHelperApi
{
    private const string IPHLPAPI = "iphlpapi.dll";

    #region Constants

    public const int ERROR_SUCCESS = 0;
    public const int ERROR_NOT_FOUND = 1168;
    public const int ERROR_OBJECT_ALREADY_EXISTS = 5010;

    // Address families
    public const ushort AF_INET = 2;
    public const ushort AF_INET6 = 23;
    public const ushort AF_UNSPEC = 0;

    // Route protocol
    public const int MIB_IPPROTO_NETMGMT = 3;
    public const int MIB_IPPROTO_LOCAL = 2;

    // Route types
    public const int MIB_IPROUTE_TYPE_INDIRECT = 4;
    public const int MIB_IPROUTE_TYPE_DIRECT = 3;

    #endregion

    #region Structures

    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_IPFORWARD_ROW2
    {
        public NET_LUID InterfaceLuid;
        public uint InterfaceIndex;
        public IP_ADDRESS_PREFIX DestinationPrefix;
        public SOCKADDR_INET NextHop;
        public byte SitePrefixLength;
        public uint ValidLifetime;
        public uint PreferredLifetime;
        public uint Metric;
        public uint Protocol;
        [MarshalAs(UnmanagedType.U1)]
        public bool Loopback;
        [MarshalAs(UnmanagedType.U1)]
        public bool AutoconfigureAddress;
        [MarshalAs(UnmanagedType.U1)]
        public bool Publish;
        [MarshalAs(UnmanagedType.U1)]
        public bool Immortal;
        public uint Age;
        public uint Origin;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NET_LUID
    {
        public ulong Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IP_ADDRESS_PREFIX
    {
        public SOCKADDR_INET Prefix;
        public byte PrefixLength;
    }

    [StructLayout(LayoutKind.Explicit, Size = 28)]
    public struct SOCKADDR_INET
    {
        [FieldOffset(0)]
        public SOCKADDR_IN Ipv4;
        [FieldOffset(0)]
        public SOCKADDR_IN6 Ipv6;
        [FieldOffset(0)]
        public ushort si_family;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SOCKADDR_IN
    {
        public ushort sin_family;
        public ushort sin_port;
        public IN_ADDR sin_addr;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public byte[] sin_zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SOCKADDR_IN6
    {
        public ushort sin6_family;
        public ushort sin6_port;
        public uint sin6_flowinfo;
        public IN6_ADDR sin6_addr;
        public uint sin6_scope_id;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IN_ADDR
    {
        public uint S_addr;

        public IN_ADDR(uint addr)
        {
            S_addr = addr;
        }

        public IN_ADDR(byte[] bytes)
        {
            if (bytes.Length != 4)
                throw new ArgumentException("IPv4 address must be 4 bytes");
            S_addr = BitConverter.ToUInt32(bytes, 0);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IN6_ADDR
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] Bytes;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_IPFORWARD_TABLE2
    {
        public uint NumEntries;
        // Followed by NumEntries of MIB_IPFORWARD_ROW2
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_IPINTERFACE_ROW
    {
        public ushort Family;
        public NET_LUID InterfaceLuid;
        public uint InterfaceIndex;
        public uint MaxReassemblySize;
        public ulong InterfaceIdentifier;
        public uint MinRouterAdvertisementInterval;
        public uint MaxRouterAdvertisementInterval;
        [MarshalAs(UnmanagedType.U1)]
        public bool AdvertisingEnabled;
        [MarshalAs(UnmanagedType.U1)]
        public bool ForwardingEnabled;
        [MarshalAs(UnmanagedType.U1)]
        public bool WeakHostSend;
        [MarshalAs(UnmanagedType.U1)]
        public bool WeakHostReceive;
        [MarshalAs(UnmanagedType.U1)]
        public bool UseAutomaticMetric;
        [MarshalAs(UnmanagedType.U1)]
        public bool UseNeighborUnreachabilityDetection;
        [MarshalAs(UnmanagedType.U1)]
        public bool ManagedAddressConfigurationSupported;
        [MarshalAs(UnmanagedType.U1)]
        public bool OtherStatefulConfigurationSupported;
        [MarshalAs(UnmanagedType.U1)]
        public bool AdvertiseDefaultRoute;
        public uint RouterDiscoveryBehavior;
        public uint DadTransmits;
        public uint BaseReachableTime;
        public uint RetransmitTime;
        public uint PathMtuDiscoveryTimeout;
        public uint LinkLocalAddressBehavior;
        public uint LinkLocalAddressTimeout;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public uint[] ZoneIndices;
        public uint SitePrefixLength;
        public uint Metric;
        public uint NlMtu;
        [MarshalAs(UnmanagedType.U1)]
        public bool Connected;
        [MarshalAs(UnmanagedType.U1)]
        public bool SupportsWakeUpPatterns;
        [MarshalAs(UnmanagedType.U1)]
        public bool SupportsNeighborDiscovery;
        [MarshalAs(UnmanagedType.U1)]
        public bool SupportsRouterDiscovery;
        public uint ReachableTime;
        public byte TransmitOffload;
        public byte ReceiveOffload;
        [MarshalAs(UnmanagedType.U1)]
        public bool DisableDefaultRoutes;
    }

    #endregion

    #region Functions

    [DllImport(IPHLPAPI, SetLastError = true)]
    public static extern int GetIpForwardTable2(ushort Family, out IntPtr Table);

    [DllImport(IPHLPAPI)]
    public static extern void FreeMibTable(IntPtr Table);

    [DllImport(IPHLPAPI, SetLastError = true)]
    public static extern int CreateIpForwardEntry2(ref MIB_IPFORWARD_ROW2 Row);

    [DllImport(IPHLPAPI, SetLastError = true)]
    public static extern int DeleteIpForwardEntry2(ref MIB_IPFORWARD_ROW2 Row);

    [DllImport(IPHLPAPI, SetLastError = true)]
    public static extern int GetBestRoute2(
        IntPtr InterfaceLuid,
        uint InterfaceIndex,
        IntPtr SourceAddress,
        ref SOCKADDR_INET DestinationAddress,
        uint AddressSortOptions,
        out MIB_IPFORWARD_ROW2 BestRoute,
        out SOCKADDR_INET BestSourceAddress);

    [DllImport(IPHLPAPI, SetLastError = true)]
    public static extern int GetBestInterfaceEx(ref SOCKADDR_INET pDestAddr, out uint pdwBestIfIndex);

    [DllImport(IPHLPAPI, SetLastError = true)]
    public static extern int GetIpInterfaceEntry(ref MIB_IPINTERFACE_ROW Row);

    [DllImport(IPHLPAPI, SetLastError = true)]
    public static extern int SetIpInterfaceEntry(ref MIB_IPINTERFACE_ROW Row);

    [DllImport(IPHLPAPI)]
    public static extern int InitializeIpForwardEntry(ref MIB_IPFORWARD_ROW2 Row);

    [DllImport(IPHLPAPI, CharSet = CharSet.Unicode)]
    public static extern int ConvertInterfaceNameToLuidW(string InterfaceName, out NET_LUID InterfaceLuid);

    [DllImport(IPHLPAPI)]
    public static extern int ConvertInterfaceIndexToLuid(uint InterfaceIndex, out NET_LUID InterfaceLuid);

    [DllImport(IPHLPAPI, CharSet = CharSet.Unicode)]
    public static extern int ConvertInterfaceLuidToNameW(ref NET_LUID InterfaceLuid, 
        [MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder InterfaceName, int Length);

    [DllImport(IPHLPAPI)]
    public static extern int ConvertInterfaceLuidToIndex(ref NET_LUID InterfaceLuid, out uint InterfaceIndex);

    #endregion

    #region Helper Methods

    public static SOCKADDR_INET CreateSockAddrInet(IPAddress address)
    {
        var result = new SOCKADDR_INET();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            result.Ipv4 = new SOCKADDR_IN
            {
                sin_family = AF_INET,
                sin_port = 0,
                sin_addr = new IN_ADDR(address.GetAddressBytes()),
                sin_zero = new byte[8]
            };
        }
        else if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            result.Ipv6 = new SOCKADDR_IN6
            {
                sin6_family = AF_INET6,
                sin6_port = 0,
                sin6_flowinfo = 0,
                sin6_addr = new IN6_ADDR { Bytes = address.GetAddressBytes() },
                sin6_scope_id = 0
            };
        }

        return result;
    }

    public static IPAddress GetAddressFromSockAddr(SOCKADDR_INET sockAddr)
    {
        if (sockAddr.si_family == AF_INET)
        {
            var bytes = BitConverter.GetBytes(sockAddr.Ipv4.sin_addr.S_addr);
            return new IPAddress(bytes);
        }
        else if (sockAddr.si_family == AF_INET6)
        {
            return new IPAddress(sockAddr.Ipv6.sin6_addr.Bytes);
        }

        return IPAddress.Any;
    }

    #endregion
}
