// Compile-check only (see check-winforms-build.sh): empty stand-ins for the MSTSCLib types that the
// RDP protocol files use in declarations. Without them csc stops after the declaration phase and
// never compiles method bodies; with them it reports method-body errors everywhere, and the script
// ignores the remaining ones inside Connection/Protocol/RDP.
namespace AxMSTSCLib
{
    internal sealed class CompileCheckPlaceholder
    {
    }
}

namespace MSTSCLib
{
    public interface MsRdpClient6NotSafeForScripting { }
    public interface MsRdpClient7NotSafeForScripting { }
    public interface MsRdpClient8NotSafeForScripting { }
    public interface MsRdpClient9NotSafeForScripting { }
    public interface IMsRdpClientSecuredSettings { }
    public interface IMsRdpDrive { }
}
