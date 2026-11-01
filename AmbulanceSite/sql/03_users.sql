SET NAMES utf8mb4;
DROP USER IF EXISTS 'dispatcher'@'localhost';
DROP USER IF EXISTS 'analyst'@'localhost';

CREATE USER 'dispatcher'@'localhost' IDENTIFIED BY 'Disp_2026!';
GRANT SELECT, INSERT, UPDATE ON ambulance_service.calls TO 'dispatcher'@'localhost';
GRANT SELECT ON ambulance_service.patients TO 'dispatcher'@'localhost';
GRANT SELECT ON ambulance_service.addresses TO 'dispatcher'@'localhost';

CREATE USER 'analyst'@'localhost' IDENTIFIED BY 'Analyst_2026!';
GRANT SELECT ON ambulance_service.addresses TO 'analyst'@'localhost';
GRANT SELECT ON ambulance_service.staff TO 'analyst'@'localhost';
GRANT SELECT ON ambulance_service.ambulances TO 'analyst'@'localhost';
GRANT SELECT ON ambulance_service.shifts TO 'analyst'@'localhost';
GRANT SELECT ON ambulance_service.shift_staff TO 'analyst'@'localhost';
GRANT SELECT ON ambulance_service.calls TO 'analyst'@'localhost';
GRANT SELECT ON ambulance_service.hospitals TO 'analyst'@'localhost';
GRANT SELECT ON ambulance_service.hospitalizations TO 'analyst'@'localhost';
GRANT SELECT ON ambulance_service.diagnoses TO 'analyst'@'localhost';
GRANT SELECT ON ambulance_service.call_diagnoses TO 'analyst'@'localhost';
