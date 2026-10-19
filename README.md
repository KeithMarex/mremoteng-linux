<p align="center">
  <img width="96" src="packaging/linux/icons/128.png" alt="">
</p>

<h1 align="center">mRemoteNG for Linux</h1>

<p align="center">
  A Linux port of <a href="https://github.com/mRemoteNG/mRemoteNG">mRemoteNG</a>, the open source, multi-protocol,
  tabbed remote connections manager.
</p>

> [!IMPORTANT]
> **This is an unofficial, community-maintained fork.** It is not made by, affiliated with or endorsed by the
> mRemoteNG project. All credit for mRemoteNG goes to its authors; see [Credits](#credits).
> Please report problems with the Linux app [here](https://github.com/KeithMarex/mremoteng-linux/issues), not to
> the mRemoteNG project.

> [!WARNING]
> **Beta.** Version 0.1.0 is the first release. Expect rough edges, and keep a backup of your connection files.

## What it is

[mRemoteNG](https://mremoteng.org) is a Windows application. This fork adds a native Linux app, built with
[Avalonia](https://avaloniaui.net) and .NET 10, that opens the same connection files (`confCons.xml`), encrypted
passwords included. A team can keep one connection file and use it from Windows mRemoteNG and from Linux.

It runs on Debian 12 and 13, Ubuntu 24.04 and Zorin OS 18, on amd64 and arm64.

## Credits

mRemoteNG is the work of its authors and contributors, and this port would not exist without them:

- **mRemote**, the original application, was written by Felix Deimel.
- **mRemoteNG** was started by Riley McArdle as its continuation.
- mRemoteNG is maintained today by [Dimitrij Gorodeckij](https://github.com/Kvarkas) and
  [many contributors](https://github.com/mRemoteNG/mRemoteNG/graphs/contributors). The full list of people,
  translators and included components is in [CREDITS.md](CREDITS.md).

Links to the original project:

- Source code: [github.com/mRemoteNG/mRemoteNG](https://github.com/mRemoteNG/mRemoteNG)
- Website: [mremoteng.org](https://mremoteng.org)
- Documentation: [mremoteng.readthedocs.io](https://mremoteng.readthedocs.io/en/latest/)

What the Linux app takes from mRemoteNG:

- the connection model and its inheritance rules;
- the `confCons.xml` reading and writing, including its encryption;
- the importers;
- the connection icons and toolbar images;
- the look of the Windows app.

Most of that code was moved from the Windows app into the shared `mRemoteNG.Core` project rather than rewritten,
so it stays the code the mRemoteNG authors wrote.

**Support the original project.** If mRemoteNG is useful to you, consider supporting its maintainer through
[PayPal](https://www.paypal.com/paypalme/mremoteng) or [Ko-fi](https://ko-fi.com/Q5Q41I7JS). These links go to
the mRemoteNG project, not to this fork.

## Features

**Connections**

- A connection tree with folders and inheritance.
- Search by name, host or description.
- Drag and drop, move up and down, and sort A–Z or Z–A.
- Favorites.
- **Wake-on-LAN:** "Wake up (Wake-on-LAN)" in the right-click menu sends a magic packet to the connection's MAC
  address; on a folder, to every connection in it that has one.
- **Host status** (View > Host Status, off until you turn it on): a green or red dot after each connection shows
  whether its server answers, checked every minute with a TCP connection to its host and port. The tooltip says why
  a server is offline.
- Import from mRemoteNG XML and CSV, `.rdp` files, Remote Desktop Connection Manager (`.rdg`), PuTTY Connection
  Manager (`.dat`) and SecureCRT (`.crt`).

**Config panel**

- Properties and inheritance, categorized or alphabetical.
- Only the properties that apply to the chosen protocol are shown.
- An optional description pane.

**Session tabs**

- **SSH** runs the system's OpenSSH (`ssh`), so your `~/.ssh/config`, keys, agent and `known_hosts` apply.
  - A stored password is handed to `ssh` through `SSH_ASKPASS`. It never appears on the command line or in the
    environment.
- **Telnet** runs the system's `telnet`.
- **Rlogin** runs the system's `rlogin` (package `rsh-redone-client`). Without it the app connects itself, but most
  rlogin servers only accept the `rlogin` client, which can use a privileged port.
- **Raw** opens a TCP connection.
- **Terminal** opens a local shell.
- **PowerShell** runs `pwsh`: a local shell, or `Enter-PSSession` to the host. On Linux, PowerShell remoting runs
  over SSH, not WinRM, so the server needs PowerShell's SSH subsystem. A stored password is handed over the same way
  as for SSH.
- Tabs show the connection's icon and tab colour. Copy with Ctrl+Shift+C, paste with Ctrl+Shift+V, and close with
  Ctrl+Shift+W.
- A session that drops keeps its tab, with a Reconnect button.

**SSH tunnels**

- A connection's **SSH Tunnel** setting works as on Windows: the SSH connection it names forwards a free local port
  to the connection's server, and the session connects through it. This works for SSH, SFTP, Telnet, Raw, Rlogin,
  RDP, VNC, ARD, External Tool connections and HTTP(S), but not for FTP.
- The tunnel's questions (an unknown host key, a password that is not stored) come up as dialogs.
- The server keeps its own name: its `~/.ssh/config` entries, its `known_hosts` entry and a trusted RDP certificate
  apply as if you connected directly.

**Remote desktop tabs: RDP**

- RDP runs FreeRDP 3 (`xfreerdp3`, package `freerdp3-x11`; on Debian 12 from bookworm-backports), shown inside the
  tab. Full screen opens in a window of its own.
- The connection's settings are passed on: resolution and smart sizing, colours, drives (your home folder, or all
  mounts), printers, smart cards, clipboard, sound and microphone, the desktop experience, console session,
  restricted admin and Remote Credential Guard, the start program, load balancing info and the Remote Desktop
  Gateway.
- A missing user name or password is asked for. The server's certificate is checked as the connection's
  authentication level says, with "trust and remember" for one this computer does not trust.
- Arguments and passwords go to `xfreerdp3` on its standard input, never on its command line.
- Click into the session to type in it; clicking anywhere else in the app takes the keyboard back.

**VNC tabs: VNC and Apple Remote Desktop**

- A VNC viewer built into the app draws the remote screen in the tab, as VncSharp does on Windows. It speaks RFB
  3.3 to 3.8 with the Raw, CopyRect, RRE, Hextile and ZRLE encodings, the remote pointer and screen-size changes.
- VNC passwords and Apple Remote Desktop logins (user name and password, as macOS Screen Sharing asks) are
  supported; a missing one is asked for.
- Smart Size (fit or stretch the screen, or show it pixel for pixel with scroll bars) and View Only work as on
  Windows. Ctrl+Alt+Del and Refresh are above the screen, and the clipboard is shared both ways (text).

**File transfer tabs: SFTP, FTP and FTPS**

These two protocols are new in this fork; Windows mRemoteNG does not have them.

- Two panes: this computer and the server.
- Click a column header (name, size, modified, permissions) to sort by it, and again for the other way round; folders
  stay on top. Drag a header's edge to make a column wider or narrower. Each pane remembers its order and widths.
- Upload and download with the buttons, by dragging between the panes, or by dragging files in from the file
  manager.
- On the server: new folder, rename, delete, and moving items by dragging them onto a folder or "..".
- A transfer queue with progress and cancel.
- Host key and certificate checks, with "trust and remember".
- "Open SFTP" on SSH2 connections, from the tree's right-click menu.

**Programs with their own window**

- **HTTP and HTTPS** open the address in your web browser (`xdg-open`).
- **AnyDesk** starts the AnyDesk client with the connection's ID, and hands it the stored password.
- **SPICE** opens a virtual machine's console in `remote-viewer` (package `virt-viewer`):
  - **Proxmox VE**: through the Proxmox API (port 8006), with an API token (`user@realm!tokenid` as the user name,
    its secret as the password) or a user and password. Set the virtual machine by ID or name; its node is found
    when you leave it empty.
  - **QEMU/libvirt**: QEMU's own SPICE port (5900 and up), with its SPICE password if it has one.
  - The password goes to `remote-viewer` in a file only you can read, which is deleted once it is read.

**External tools**

- Tools > External Tools keeps a list of programs to run on a connection, in the Windows app's format
  (`~/.config/mRemoteNG/extApps.xml`, so a Windows `extApps.xml` can be copied over).
- The arguments can hold the connection's details: `%HOSTNAME%`, `%PORT%`, `%USERNAME%`, `%PASSWORD%` and the other
  variables of the Windows app, and environment variables. They are split as a shell would, but no shell runs, so a
  value never becomes an extra argument.
- Run a tool from a connection's right-click menu (on a folder: for each connection in it), as an External Tool
  connection, or before and after a connection (Pre- and Post-external tool). A tool set to run integrated runs in a
  terminal tab.

**Options**

- The connection file format version: 2.7, 2.8 or 2.9.
- Saving after every edit.
- Encryption settings, including encrypting the whole file.
- Theme: system, light or dark.

## Not supported yet

- **External tools** cannot run elevated, and integrated tools run in a terminal tab rather than with their own
  window inside the tab. There is no external tools toolbar.
- **VNC:** the VNC proxy settings, Tight encoding and VeNCrypt/TLS logins are not supported.
- **RDP:** serial ports, drive letters, the idle timeout, Hyper-V enhanced session mode and external credential
  providers are not supported.
- **WSL** only exists on Windows; opening such a connection says so.
- **SSH1** is refused, because OpenSSH no longer supports it.
- **PuTTY-specific SSH options** are dropped, and the connection's opening command is not sent yet.
- **Telnet** does not log in with the stored user name and password.
- **SFTP** does not use the SSH agent or `ProxyJump`/`ProxyCommand` from `ssh_config`.
- **SPICE:** a Proxmox VE console cannot go through an SSH tunnel (it uses Proxmox's SPICE proxy on port 3128), and
  QEMU's SPICE port with TLS is not supported.
- The app is in English only, has no Help or About window yet, and is packaged only as a `.deb`.

## Install

1. Download `mremoteng_<version>_amd64.deb` (or `_arm64.deb`) and `SHA256SUMS` from the
   [Releases](https://github.com/KeithMarex/mremoteng-linux/releases) page.
2. Check the download and install it:

   ```bash
   sha256sum -c SHA256SUMS --ignore-missing
   sudo apt install ./mremoteng_0.1.0_amd64.deb
   ```

3. Start **mRemoteNG** from the applications menu, or run `mremoteng`.

The package contains everything .NET needs (it is self-contained), so you do not have to install .NET. It
depends on `openssh-client`, recommends `inetutils-telnet`, `xdg-utils` and `freerdp3-x11`, and suggests
`rsh-redone-client` (for rlogin) and `virt-viewer` (for SPICE). PowerShell (`pwsh`) and AnyDesk come from Microsoft
and AnyDesk.

To remove the app, run `sudo apt remove mremoteng`. Your connection files and settings are left in place.

## Sharing connection files with Windows

The Linux app writes connection files that Windows mRemoteNG can open.

- **File format.** By default it writes file format 2.7, which Windows mRemoteNG 1.77 and later read and write.
  You can choose 2.8 (Windows 1.78.1 and later) or 2.9 in Options.
- **Passwords.** Passwords are encrypted the same way as on Windows. The file password (master password) is set
  with the Password property of the root node.
- **SFTP, FTP and SPICE connections.** Windows mRemoteNG does not know these protocols, so they are stored in a
  way it can read:
  - In `confCons.xml`, FTP is saved as RAW, SFTP as SSH2 and SPICE as VNC.
  - The real protocol is saved in the node's `PluginData` (as `mRx.*` values), which newer Windows versions keep.
  - It is also saved in a small file next to the connection file, `confCons.xml.linux.json`, for Windows versions
    that drop `PluginData`. Keep that file together with the connection file.
  - If someone changes the protocol on Windows, that change wins.
  - **Limitation:** a *folder's* SFTP, FTP or SPICE protocol is lost once Windows saves the file, because Windows
    gives folders new IDs.

## Where files are kept

| What | Where |
|---|---|
| Connection file | `~/.local/share/mRemoteNG/confCons.xml`. You can open another one with File > Open. |
| Backups | Next to the connection file, as `<file>.<date-time>.backup`. The last 10 are kept. |
| SFTP, FTP and SPICE details for older Windows versions | Next to the connection file, as `<file>.linux.json`. |
| Settings | `~/.config/mRemoteNG/settings.json` |
| External tools | `~/.config/mRemoteNG/extApps.xml` |
| Log | `~/.local/state/mRemoteNG/mRemoteNG.log` |

These follow `XDG_DATA_HOME`, `XDG_CONFIG_HOME` and `XDG_STATE_HOME` when those are set.

## Build from source

You need the .NET SDK version named in [global.json](global.json) (10.0).

```bash
dotnet build mRemoteNG.Linux.slnf -c Release
dotnet test mRemoteNG.Linux.slnf -c Release --no-build
dotnet run --project mRemoteNG.Avalonia
```

[mRemoteNG.Linux.slnf](mRemoteNG.Linux.slnf) contains only the projects that build on Linux. The full
`mRemoteNG.sln` also contains the Windows app, which needs Windows and Visual Studio to build.

To build the Debian package:

```bash
scripts/linux/build-deb.sh amd64            # or arm64; the package goes to artifacts/
```

This needs `dpkg-deb`, `python3` and `strip`. For arm64 it also needs `binutils-aarch64-linux-gnu`.

To test the package, run [scripts/linux/test-deb.sh](scripts/linux/test-deb.sh) as root in a clean Debian or
Ubuntu container. It installs the package, starts the app on a virtual X server and removes it again.

[.github/workflows/linux-release.yml](.github/workflows/linux-release.yml) does all of this in CI. On a `v*` tag it
publishes the release.

## How this fork is organised

| Project | What it is |
|---|---|
| `mRemoteNG.Core` | The platform-neutral parts of mRemoteNG (connections, files, encryption, importers), moved out of the Windows app. Both apps use it. |
| `mRemoteNG.Avalonia` | The Linux app. |
| `mRemoteNG.FileTransfer` | The SFTP and FTP/FTPS engine. |
| `mRemoteNG.Vnc` | The VNC client (with Apple Remote Desktop logins) behind the VNC tabs. |
| `*.Tests` | Tests for the projects above. |
| `mRemoteNG` | The Windows app, as in mRemoteNG, except that it uses `mRemoteNG.Core`. |
| `packaging/`, `scripts/linux/` | The Debian package. |

Other notes:

- This fork does not publish Windows builds. On Windows, use the
  [official mRemoteNG releases](https://github.com/mRemoteNG/mRemoteNG/releases).
- Changes from mRemoteNG are merged in regularly.
- Changes that are useful to mRemoteNG itself, such as fixes in the shared code, are meant to be offered upstream.

## License

mRemoteNG for Linux is licensed under the GNU General Public License, version 2, the same license as mRemoteNG.
See [COPYING.txt](COPYING.txt). The licenses of the bundled libraries are listed in the package's
`/usr/share/doc/mremoteng/copyright`.

The mRemoteNG name and logo are those of the mRemoteNG project. This fork uses them only to say what it is a port
of.
