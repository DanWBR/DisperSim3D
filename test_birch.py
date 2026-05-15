import math

# Gant & Ivings case parameters
d_real = 0.0105  # 10.5 mm
P_vessel = 5.0 * 101325.0  # 5.0 bar abs in Pa
T_vessel = 250.0  # K
M_gas = 0.016  # CH4 molar mass in kg/mol
gamma = 1.4  # diatomic gas
P_amb = 101325.0  # 1 atm
Cd = 0.65  # discharge coefficient
R = 8.314  # universal gas constant

print("=== Gant & Ivings Case (10.5mm orifice, 5.0 bar, 250K CH4) ===")
print(f"Orifice diameter (real): {d_real * 1000:.1f} mm")
print(f"Vessel pressure: {P_vessel / 101325:.2f} bar abs ({P_vessel:.0f} Pa)")
print(f"Vessel temperature: {T_vessel:.0f} K")
print()

# Check if choked
critical_ratio = (2.0 / (gamma + 1)) ** (gamma / (gamma - 1))
pressure_ratio = P_amb / P_vessel
is_choked = pressure_ratio <= critical_ratio

print(f"Critical pressure ratio: {critical_ratio:.4f}")
print(f"Actual P_amb/P_vessel: {pressure_ratio:.4f}")
print(f"Flow is CHOKED (sonic): {is_choked}")
print()

# Calculate real mass flow (choked case)
A = math.pi * 0.25 * d_real * d_real
factor = gamma * M_gas / (R * T_vessel)
term = (2.0 / (gamma + 1)) ** ((gamma + 1) / (gamma - 1))
mdot_real = Cd * A * P_vessel * math.sqrt(factor * term)

print(f"Real orifice area: {A * 1e6:.3f} mm²")
print(f"Mass flow rate (real orifice): {mdot_real:.6f} kg/s")
print()

# Birch & Schefer expanded source
v_target = 100.0  # target velocity m/s
T_ambient = 293.15  # K
rho_ambient = P_amb * M_gas / (R * T_ambient)
A_expanded = mdot_real / (rho_ambient * v_target)
d_expanded = math.sqrt(4.0 * A_expanded / math.pi)

print("=== Birch & Schefer Expanded Source ===")
print(f"Target velocity: {v_target:.1f} m/s")
print(f"Ambient density: {rho_ambient:.4f} kg/m³")
print(f"Expanded area: {A_expanded * 1e6:.2f} mm²")
print(f"Expanded diameter: {d_expanded * 1000:.3f} mm")
print(f"Temperature at source: {T_ambient:.2f} K")
print()
print(f"EXPANSION RATIO: {d_expanded / d_real:.2f}x")
print(f"Area expansion: {A_expanded / A:.2f}x")

