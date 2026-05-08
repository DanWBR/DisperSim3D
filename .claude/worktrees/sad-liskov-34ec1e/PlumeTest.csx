// C# script to test GaussianPlumeEngine with the user's test case parameters
// Run with: dotnet-script PlumeTest.csx or manually compute

// Test case parameters from teste_disp1.xml:
// Source: Position (0,0,0), HeightOffset=2 → EffectivePosition=(0,0,2)
// Azimuth=0, Elevation=0 → ReleaseDirection=(0,1,0) (North)
// Wind: 270°, Speed=0.1 m/s, Stability D
// HPLeak: VesselP=1MPa, Orifice=0.01m, Gamma=1.4, MolarMass=0.01604
// Q=0.5 kg/s, Domain=200m, GridRes=200

// ---- Manual calculations ----

// 1. Wind direction (270°): windDirRad = 270 * PI/180 = 4.712
//    WindDir3D = (sin(4.712), cos(4.712), 0) = (-1, 0, 0)  → wind blows West
//    (meteorological convention: 270 = FROM West, so wind goes East?
//     Actually sin(270°)=-1, cos(270°)=0, so windDir = (-1, 0, 0))

// 2. Release direction: Azimuth=0, Elevation=0
//    azRad = 0, elRad = 0, cosEl = 1
//    ReleaseDir = (cos(0)*sin(0), cos(0)*cos(0), sin(0)) = (0, 1, 0) → North

// 3. Dot product releaseDir · windDir = 0*(-1) + 1*0 = 0 < 0.99 → hasDifferentDirection = TRUE

// 4. HPLeak computed exit velocity:
//    gamma=1.4, M=0.01604, T=293.15
//    Check if choked: critRatio = (2/(1.4+1))^(1.4/0.4) = (2/2.4)^3.5 = 0.8333^3.5 = 0.5283
//    P_amb/P_vessel = 101325/1000000 = 0.101325 < 0.5283 → CHOKED
//    exitVel = sqrt(gamma * R * T / M * (2/(gamma+1)))
//            = sqrt(1.4 * 8.314 * 293.15 / 0.01604 * (2/2.4))
//            = sqrt(1.4 * 8.314 * 293.15 / 0.01604 * 0.8333)
//            = sqrt(213298.7)
//            ≈ 461.8 m/s

// 5. Effective diameter = orifice = 0.01m (since StackDiameterM = 0)

// 6. Briggs plume rise:
//    windAtStack = WindSpeedAtHeight(2) = 0.1 * (2/10)^0.15 = 0.1 * 0.200^0.15
//    0.200^0.15 = exp(0.15 * ln(0.2)) = exp(0.15 * (-1.609)) = exp(-0.2414) = 0.786
//    windAtStack = 0.1 * 0.786 = 0.0786 → clamped to 0.5 m/s in Briggs
//
//    ExitTemp = 293.15 = AmbientTemp → No buoyancy rise (ts <= ta)
//    Momentum rise: 1.44 * (vs*ds/u)^(2/3) * ds^(1/3)
//      = 1.44 * (461.8 * 0.01 / 0.5)^(2/3) * 0.01^(1/3)
//      = 1.44 * (9.236)^(2/3) * 0.2154
//      = 1.44 * 4.423 * 0.2154
//      = 1.372 m
//
//    H = baseHeight + deltaH = 2 + 1.372 = 3.372 m
//    Capped at mixingHeight=1000 → H = 3.372

// 7. Wind speed at H=3.372:
//    WindSpeedAtHeight(3.372) = 0.1 * (3.372/10)^0.15 = 0.1 * 0.3372^0.15
//    0.3372^0.15 = exp(0.15 * ln(0.3372)) = exp(0.15 * (-1.088)) = exp(-0.1632) = 0.849
//    windSpeed = 0.1 * 0.849 = 0.0849 → clamped to 0.5 m/s (MinWindSpeed)

// 8. Bend length calculation:
//    exitVel = 461.8, windSpeed = 0.5, effDiam = 0.01
//    bendLength = max(461.8/0.5, 1.0) * 0.01 * 5.0 = 923.6 * 0.01 * 5.0 = 46.18 m

// 9. Trajectory: starts at (0, 0, 3.372), initial direction (0,1,0), curves to (-1,0,0)
//    ds = domainSize*2.5/200 = 200*2.5/200 = 2.5 m per step
//    At arcLen=0: blend = 1-exp(0) = 0, pure release dir (0,1,0)
//    At arcLen=46.18: blend = 1-exp(-1) = 0.632, mostly wind dir
//    At arcLen=230.9 (5*bend): blend = 1-exp(-5) = 0.993, almost pure wind

// 10. Gaussian concentration:
//     For stability D, sigma coefficients: ay=0.1471, by=0.9005, az=0.079, bz=0.8855
//     At downwind distance x:
//       sigmaY = 0.1471 * x^0.9005
//       sigmaZ = 0.079 * x^0.8855
//
//     At x=10m: sigmaY = 0.1471 * 10^0.9005 = 0.1471 * 7.95 = 1.17m
//               sigmaZ = 0.079 * 10^0.8855 = 0.079 * 7.69 = 0.608m
//     At x=50m: sigmaY = 0.1471 * 50^0.9005 = 0.1471 * 35.4 = 5.21m
//               sigmaZ = 0.079 * 50^0.8855 = 0.079 * 32.5 = 2.57m
//     At x=100m: sigmaY = 0.1471 * 100^0.9005 = 0.1471 * 63.4 = 9.33m
//                sigmaZ = 0.079 * 100^0.8855 = 0.079 * 58.7 = 4.64m

// 11. Centerline concentration (crosswind=0, z=H):
//     C = Q / (2*PI*u*sigY*sigZ) * exp(0) * vertTerm
//     vertTerm at z=H: exp(0) + exp(-(2H)^2/(2*sigZ^2))
//
//     At x=10m: C = 0.5 / (6.283 * 0.5 * 1.17 * 0.608) * (1 + exp(-4*H^2/(2*sigZ^2)))
//             = 0.5 / (2.236) * (1 + exp(-4*3.372^2/(2*0.608^2)))
//             = 0.2236 * (1 + exp(-61.4))
//             ≈ 0.224 kg/m³
//     At x=50m: C = 0.5 / (6.283 * 0.5 * 5.21 * 2.57) * vertTerm
//             = 0.5 / (42.11) * (1 + exp(-4*3.372^2/(2*2.57^2)))
//             = 0.01187 * (1 + exp(-3.44))
//             = 0.01187 * (1 + 0.0321)
//             ≈ 0.01225 kg/m³
//     At x=100m: C = 0.5 / (6.283 * 0.5 * 9.33 * 4.64) * vertTerm
//              = 0.5 / (136.2) * (1 + exp(-4*3.372^2/(2*4.64^2)))
//              = 0.00367 * (1 + exp(-1.055))
//              = 0.00367 * (1 + 0.349)
//              ≈ 0.00495 kg/m³

// 12. Grid resolution check:
//     GridRes=200, DomainSize=200m → grid spans [-200, 200] → total 400m
//     cellSize = 400/200 = 2m
//     nz = GridRes/2 = 100 → vertical range 0-200m
//
//     At x=10m near source, sigmaY=1.17m, but grid cell is 2m → plume barely resolved
//     The peak concentration sits within ~1 cell of the centerline
//     Many grid cells near source will have crosswind > sigmaY → zero concentration
//
//     At x=100m, sigmaY=9.33m → about 4-5 cells → reasonable resolution

// 13. KEY ISSUE: The trajectory starts going North (Y+), then bends West (X-).
//     The grid origin is at (-200, -200, 0). Source is at (0, 0, 3.372).
//     Grid cell (100, 100, 1) maps to world (0, 0, 2), roughly near source.
//     The plume extends mainly in +Y (North) near source, then curves -X (West).
//
//     With bent trajectory at 46m bending length:
//     - First ~50m: mostly North → Y increasing
//     - Then curves toward X=-1 direction → X decreasing
//     - After ~200m: nearly pure West direction
//
//     This SHOULD produce a curved plume, but:
//     - If the grid sampling misses the narrow plume near source (sigmaY < cellSize)
//     - The auto-threshold sampling at line 393 first calls GenerateIsosurfaces with
//       EMPTY thresholds list → this samples the field but generates NO isosurfaces
//     - Then GetMaxConcentration() reads the sampled field max
//
//     BUT WAIT: line 392 has "\ First pass" instead of "// First pass"
//     This would be a SYNTAX ERROR! The backslash is not valid C# comment syntax.
//     If this compiles, it must be an artifact of display... let me check.

// 14. CONCLUSION: The numbers should be reasonable. Max concentration near source
//     would be ~0.22 kg/m³ = 220 g/m³. For methane (MW=16), this is
//     0.22/0.016 * 0.0224 = 0.308 m³/m³ = 30.8% vol → very high but this is
//     right at the source of a high-pressure leak.
//
//     Auto-thresholds would be:
//     High = 0.11 kg/m³, Medium = 0.022 kg/m³, Low = 0.0022 kg/m³
//
//     The plume should extend:
//     - High threshold: maybe 20-30m along trajectory
//     - Medium: maybe 70-100m
//     - Low: maybe 200-300m
//
//     These should be visible in the 400m domain.

Console.WriteLine("=== Manual calculation results for teste_disp1.xml ===");
Console.WriteLine();

double gamma = 1.4;
double M = 0.01604;
double T = 293.15;
double R = 8.314;

// Exit velocity (choked)
double exitVel = Math.Sqrt(gamma * R * T / M * (2.0 / (gamma + 1)));
Console.WriteLine($"Exit velocity (choked): {exitVel:F1} m/s");

// Briggs momentum rise
double ds_stack = 0.01;
double u_stack = 0.5; // clamped
double deltaH = 1.44 * Math.Pow(exitVel * ds_stack / u_stack, 2.0/3.0) * Math.Pow(ds_stack, 1.0/3.0);
Console.WriteLine($"Plume rise (momentum): {deltaH:F2} m");
double H = 2.0 + deltaH;
Console.WriteLine($"Effective height H: {H:F2} m");

// Bend length
double bendLen = Math.Max(exitVel / 0.5, 1.0) * 0.01 * 5.0;
Console.WriteLine($"Bend length: {bendLen:F1} m");

Console.WriteLine();
Console.WriteLine("Centerline concentrations along trajectory:");

double Q = 0.5;
double u = 0.5;
double twoPi = 2.0 * Math.PI;

foreach (double x in new[] { 5, 10, 20, 50, 100, 200, 500 })
{
    double sigY = 0.1471 * Math.Pow(x, 0.9005);
    double sigZ = 0.079 * Math.Pow(x, 0.8855);
    if (sigY < 0.5) sigY = 0.5;
    if (sigZ < 0.5) sigZ = 0.5;

    double dz1 = 0; // z = H on centerline
    double dz2 = 2.0 * H;
    double invSz2 = 1.0 / (2.0 * sigZ * sigZ);
    double vertTerm = Math.Exp(-dz1 * dz1 * invSz2) + Math.Exp(-dz2 * dz2 * invSz2);

    double C = Q / (twoPi * u * sigY * sigZ) * vertTerm;
    Console.WriteLine($"  x={x,4}m: sigY={sigY:F2}m, sigZ={sigZ:F2}m, C={C:E3} kg/m³");
}

Console.WriteLine();
Console.WriteLine("Grid analysis:");
double domainSize = 200.0;
int gridRes = 200;
double cellSize = (domainSize * 2.0) / gridRes;
int nz = gridRes / 2;
Console.WriteLine($"  Cell size: {cellSize:F1}m");
Console.WriteLine($"  Grid: {gridRes}x{gridRes}x{nz} = {(long)gridRes*gridRes*nz:N0} cells");
Console.WriteLine($"  Domain: [{-domainSize}, {domainSize}] in X,Y; [0, {nz*cellSize}] in Z");
Console.WriteLine();

// Check which grid cells the trajectory passes through
Console.WriteLine("Trajectory points (first 20):");
double cx = 0, cy = 0, cz = H;
double arcLen = 0;
double dsStep = domainSize * 2.5 / 200;
for (int i = 0; i < 20; i++)
{
    double blend = bendLen > 0 ? 1.0 - Math.Exp(-arcLen / bendLen) : 1.0;
    double dirX = 0 * (1 - blend) + (-1) * blend;
    double dirY = 1 * (1 - blend) + 0 * blend;
    double dirZ = 0;
    double mag = Math.Sqrt(dirX*dirX + dirY*dirY + dirZ*dirZ);
    if (mag > 1e-10) { dirX /= mag; dirY /= mag; dirZ /= mag; }

    // Grid indices
    int gi = (int)((cx - (-domainSize)) / cellSize);
    int gj = (int)((cy - (-domainSize)) / cellSize);
    int gk = (int)((cz - 0) / cellSize);

    Console.WriteLine($"  [{i,3}] arc={arcLen:F1}m pos=({cx:F1},{cy:F1},{cz:F1}) blend={blend:F3} dir=({dirX:F3},{dirY:F3}) grid=({gi},{gj},{gk})");

    cx += dirX * dsStep;
    cy += dirY * dsStep;
    cz += dirZ * dsStep;
    arcLen += dsStep;
}
