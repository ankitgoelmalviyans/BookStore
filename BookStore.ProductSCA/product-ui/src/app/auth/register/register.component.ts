import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-register',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
  <mat-card>
    <h2>Register</h2>
    <form [formGroup]="registerForm" (ngSubmit)="register()" *ngIf="!submitted">
      <mat-form-field appearance="fill">
        <mat-label>Username</mat-label>
        <input matInput formControlName="username" required>
      </mat-form-field>
      <mat-form-field appearance="fill">
        <mat-label>Password</mat-label>
        <input matInput type="password" formControlName="password" required>
      </mat-form-field>
      <button mat-raised-button color="primary">Register</button>
    </form>
    <p *ngIf="submitted">{{ message }}</p>
    <a routerLink="/login">Back to login</a>
  </mat-card>
  `
})
export class RegisterComponent {
  registerForm = this.fb.group({ username: [''], password: [''] });
  submitted = false;
  message = '';

  constructor(private fb: FormBuilder, private auth: AuthService, private router: Router) {}

  register() {
    this.auth.register(this.registerForm.value).subscribe({
      next: (res: any) => {
        this.submitted = true;
        this.message = res?.message ?? 'Registration submitted. An administrator must activate your account before you can log in.';
      },
      error: (err: any) => {
        this.submitted = true;
        this.message = err?.error?.message ?? 'Registration failed. Please try again.';
      }
    });
  }
}
