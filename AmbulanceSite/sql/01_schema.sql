SET NAMES utf8mb4;
DROP DATABASE IF EXISTS ambulance_service;
CREATE DATABASE ambulance_service CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
USE ambulance_service;

CREATE TABLE addresses (
    address_id INT AUTO_INCREMENT PRIMARY KEY,
    city VARCHAR(100) NOT NULL,
    street VARCHAR(100) NOT NULL,
    building VARCHAR(10) NOT NULL
);

CREATE TABLE staff (
    staff_id INT AUTO_INCREMENT PRIMARY KEY,
    full_name VARCHAR(150) NOT NULL,
    position VARCHAR(50) NOT NULL,
    qualification VARCHAR(50),
    phone VARCHAR(20)
);

CREATE TABLE ambulances (
    vehicle_id INT AUTO_INCREMENT PRIMARY KEY,
    plate_number VARCHAR(15) NOT NULL UNIQUE,
    model VARCHAR(50) NOT NULL,
    vehicle_type VARCHAR(30) NOT NULL,
    year INT NOT NULL
);

CREATE TABLE diagnoses (
    diagnosis_id INT AUTO_INCREMENT PRIMARY KEY,
    icd_code VARCHAR(10) NOT NULL UNIQUE,
    name VARCHAR(150) NOT NULL
);

CREATE TABLE patients (
    patient_id INT AUTO_INCREMENT PRIMARY KEY,
    full_name VARCHAR(150) NOT NULL,
    birth_date DATE NOT NULL,
    phone VARCHAR(20),
    blood_type VARCHAR(5),
    address_id INT NOT NULL,
    FOREIGN KEY (address_id) REFERENCES addresses(address_id)
);

CREATE TABLE hospitals (
    hospital_id INT AUTO_INCREMENT PRIMARY KEY,
    name VARCHAR(150) NOT NULL,
    specialization VARCHAR(100),
    phone VARCHAR(20),
    address_id INT NOT NULL,
    FOREIGN KEY (address_id) REFERENCES addresses(address_id)
);

CREATE TABLE shifts (
    shift_id INT AUTO_INCREMENT PRIMARY KEY,
    shift_date DATE NOT NULL,
    start_time DATETIME NOT NULL,
    end_time DATETIME,
    crew_type ENUM('лікарська','фельдшерська','парамедиків') NOT NULL,
    status ENUM('на базі','на виклику','технічне обслуговування') NOT NULL DEFAULT 'на базі',
    vehicle_id INT NOT NULL,
    FOREIGN KEY (vehicle_id) REFERENCES ambulances(vehicle_id)
);

CREATE TABLE shift_staff (
    shift_id INT NOT NULL,
    staff_id INT NOT NULL,
    role_in_shift VARCHAR(50) NOT NULL,
    PRIMARY KEY (shift_id, staff_id),
    FOREIGN KEY (shift_id) REFERENCES shifts(shift_id),
    FOREIGN KEY (staff_id) REFERENCES staff(staff_id)
);

CREATE TABLE calls (
    call_id INT AUTO_INCREMENT PRIMARY KEY,
    call_time DATETIME NOT NULL,
    symptoms VARCHAR(255),
    priority ENUM('високий','середній','низький') NOT NULL,
    status ENUM('зареєстровано','в дорозі','виконано','скасовано') NOT NULL DEFAULT 'зареєстровано',
    arrival_time DATETIME,
    patient_id INT NOT NULL,
    address_id INT NOT NULL,
    dispatcher_id INT NOT NULL,
    shift_id INT,
    FOREIGN KEY (patient_id) REFERENCES patients(patient_id),
    FOREIGN KEY (address_id) REFERENCES addresses(address_id),
    FOREIGN KEY (dispatcher_id) REFERENCES staff(staff_id),
    FOREIGN KEY (shift_id) REFERENCES shifts(shift_id),
    CONSTRAINT chk_arrival_time CHECK (arrival_time IS NULL OR arrival_time >= call_time)
);

CREATE TABLE hospitalizations (
    hospitalization_id INT AUTO_INCREMENT PRIMARY KEY,
    delivery_time DATETIME NOT NULL,
    department VARCHAR(100) NOT NULL,
    admission_status VARCHAR(50) NOT NULL,
    call_id INT NOT NULL,
    hospital_id INT NOT NULL,
    previous_hospitalization_id INT,
    FOREIGN KEY (call_id) REFERENCES calls(call_id),
    FOREIGN KEY (hospital_id) REFERENCES hospitals(hospital_id),
    FOREIGN KEY (previous_hospitalization_id) REFERENCES hospitalizations(hospitalization_id)
);

CREATE TABLE call_diagnoses (
    call_id INT NOT NULL,
    diagnosis_id INT NOT NULL,
    diagnosis_type VARCHAR(20) NOT NULL,
    PRIMARY KEY (call_id, diagnosis_id),
    FOREIGN KEY (call_id) REFERENCES calls(call_id),
    FOREIGN KEY (diagnosis_id) REFERENCES diagnoses(diagnosis_id)
);
