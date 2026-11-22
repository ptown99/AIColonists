{
  description = "AI-Integrated Colonists (RimWorld mod)";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs = { self, nixpkgs }:
    let
      systems = [ "x86_64-linux" "aarch64-linux" ];
      forAllSystems = f: nixpkgs.lib.genAttrs systems (system: f (import nixpkgs {
        inherit system;
        # requireFile marks its files unfree; allow just the game DLLs (see rimworld-refs) rather than all of unfree.
        config.allowUnfreePredicate = pkg: builtins.elem (pkg.name or "") [ "Assembly-CSharp.dll" "UnityStubs.dll" ];
      }));
    in
    {
      # `nix develop`: dotnet + env tweaks so the plain `dotnet build` / `dotnet run` commands from the README work.
      devShells = forAllSystems (pkgs: {
        default = pkgs.mkShell {
          packages = [ pkgs.dotnetCorePackages.sdk_10_0 ];
          DOTNET_CLI_TELEMETRY_OPTOUT = "1";
          DOTNET_NOLOGO = "1";
          # Tests/ and Tools/PromptProbe target net5.0, which no longer ships a runtime; let them run on .NET 10.
          DOTNET_ROLL_FORWARD = "Major";
          # MSBuild reads env vars as properties. The generic-linux apphost can't run on NixOS, so skip it;
          # `dotnet run` still works by launching the DLL through the dotnet host.
          UseAppHost = "false";
        };
      });

      packages = forAllSystems (pkgs: rec {
        # The game DLLs the mod compiles against. They're proprietary, so Nix can't download them; add your own
        # copies to the store once (see `message`). The hashes pin a game build — after a RimWorld update, re-add
        # the new files and bump the hashes (nix hash file --sri <file>).
        rimworld-refs =
          let
            gameFile = name: hash: pkgs.requireFile {
              inherit name hash;
              message = ''
                ${name} from RimWorld 1.7.5014 rev1195 (Waylith/Linux) isn't in the Nix store yet. Add it with:

                  nix-store --add-fixed sha256 ~/.local/share/Steam/steamapps/common/RimWorld/${name}

                If that fails with a hash mismatch, your game is a different version: update the hash in flake.nix.
              '';
            };
          in
          pkgs.linkFarm "rimworld-refs" {
            "Assembly-CSharp.dll" = gameFile "Assembly-CSharp.dll" "sha256-rtNpgKCmoEaUMtLNBi4JFxArABnQevyDKyyVnDYic7s=";
            "UnityStubs.dll" = gameFile "UnityStubs.dll" "sha256-xB3TISFUIGTMc6VjFusOb9NJWAgqCOMz2yjum/bTt/I=";
          };

        # `nix build`: the ready-to-use mod folder (copy or symlink result/ into RimWorld/Mods/).
        default = mod;
        mod = pkgs.buildDotnetModule {
          pname = "aicolonists";
          version = "0.0.0";
          src = pkgs.lib.fileset.toSource {
            root = ./.;
            fileset = pkgs.lib.fileset.unions [ ./About ./Defs ./Languages ./Patches ./Source ./0Harmony ./LICENSE ];
          };
          projectFile = "Source/AIColonists.csproj";
          # Regenerate after changing NuGet references: nix build .#mod.fetch-deps && ./result nix/mod-deps.json
          nugetDeps = ./nix/mod-deps.json;
          dotnet-sdk = pkgs.dotnetCorePackages.sdk_10_0;
          # rimworld-refs holds UnityStubs.dll, so the csproj picks its Waylith layout.
          dotnetBuildFlags = [ "-p:RimWorldDir=${rimworld-refs}" ];
          # The csproj writes straight into ../Assemblies/; ship the whole mod tree instead of `dotnet publish` output.
          installPhase = ''
            runHook preInstall
            mkdir -p $out
            cp -r About Assemblies Defs Languages Patches LICENSE $out/
            runHook postInstall
          '';
        };

        # Pure, sandboxed build of the no-game test harness (needs no RimWorld install).
        tests = pkgs.buildDotnetModule {
          pname = "aicolonists-tests";
          version = "0.0.0";
          src = pkgs.lib.fileset.toSource {
            root = ./.;
            fileset = pkgs.lib.fileset.unions [ ./Source ./Tests ];
          };
          projectFile = "Tests/AIColonists.Tests.csproj";
          # Regenerate after changing NuGet references: nix build .#tests.fetch-deps && ./result nix/deps.json
          nugetDeps = ./nix/deps.json;
          dotnet-sdk = pkgs.dotnetCorePackages.sdk_10_0;
          dotnet-runtime = pkgs.dotnetCorePackages.runtime_10_0;
          executables = [ "AIColonists.Tests" ];
          makeWrapperArgs = [ "--set" "DOTNET_ROLL_FORWARD" "Major" ];
        };
      });

      # `nix flake check`: run the harness. It locates Defs/ by walking up from its own binary to a folder holding
      # both Defs/ and Source/, so stage the binary inside a tiny copy of the mod tree first.
      checks = forAllSystems (pkgs: {
        tests = pkgs.runCommand "aicolonists-tests-run" { nativeBuildInputs = [ pkgs.dotnetCorePackages.runtime_10_0 ]; } ''
          mkdir -p mod/Tests/bin
          cp -r ${./Defs} mod/Defs
          cp -r ${./Source} mod/Source
          cp -r ${self.packages.${pkgs.system}.tests}/lib/aicolonists-tests/. mod/Tests/bin/
          DOTNET_ROLL_FORWARD=Major dotnet mod/Tests/bin/AIColonists.Tests.dll | tee $out
          grep -q ', 0 failed$' $out
        '';
      });

      apps = forAllSystems (pkgs:
        let
          sdk = pkgs.dotnetCorePackages.sdk_10_0;
          mkApp = name: text: {
            type = "app";
            program = pkgs.lib.getExe (pkgs.writeShellApplication {
              inherit name text;
              runtimeInputs = [ sdk ];
              runtimeEnv = {
                DOTNET_CLI_TELEMETRY_OPTOUT = "1";
                DOTNET_NOLOGO = "1";
                DOTNET_ROLL_FORWARD = "Major";
                UseAppHost = "false";
              };
            });
          };
        in
        {
          # `nix run .#build [-- extra dotnet args]` — builds the mod DLL into Assemblies/.
          # Impure by nature: it compiles against YOUR game's Assembly-CSharp.dll, which can't live in the store.
          build = mkApp "aicolonists-build" ''
            root=$(git rev-parse --show-toplevel 2>/dev/null || pwd)
            rimworld=''${RIMWORLD_DIR:-$HOME/.local/share/Steam/steamapps/common/RimWorld}
            if [ ! -e "$rimworld/Assembly-CSharp.dll" ] && [ ! -d "$rimworld/RimWorldLinux_Data" ] && [ ! -d "$rimworld/RimWorldWin64_Data" ]; then
              echo "RimWorld not found at $rimworld; set RIMWORLD_DIR=/path/to/RimWorld" >&2
              exit 1
            fi
            exec dotnet build "$root/Source/AIColonists.csproj" -c Release -p:RimWorldDir="$rimworld" "$@"
          '';

          # `nix run .#test` — builds + runs the no-game harness from the working tree (no sandbox, fast iteration).
          test = mkApp "aicolonists-test" ''
            root=$(git rev-parse --show-toplevel 2>/dev/null || pwd)
            exec dotnet run --project "$root/Tests/AIColonists.Tests.csproj" -c Release "$@"
          '';
        });
    };
}
