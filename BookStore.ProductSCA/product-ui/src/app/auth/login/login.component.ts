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
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
  <mat-card>
    <h2>Login</h2>
    <form [formGroup]="loginForm" (ngSubmit)="login()">
      <mat-form-field appearance="fill">
        <mat-label>Email</mat-label>
        <input matInput formControlName="email" required>
      </mat-form-field>
      <mat-form-field appearance="fill">
        <mat-label>Password</mat-label>
        <input matInput type="password" formControlName="password" required>
      </mat-form-field>
      <p *ngIf="errorMessage">{{ errorMessage }}</p>
      <button mat-raised-button color="primary">Login</button>
    </form>
    <a routerLink="/register">Register</a>
  </mat-card>
  `
})
export class LoginComponent {
  loginForm = this.fb.group({ email: [''], password: [''] });
  errorMessage = '';

  constructor(private fb: FormBuilder, private auth: AuthService, private router: Router) {}

  login() {
    this.errorMessage = '';
    const body = {
      username: this.loginForm.value.email, //map email field to username
      password: this.loginForm.value.password
    };
    this.auth.login(body).subscribe({
      next: (res: any) => {
        this.auth.saveToken(res.token);
        this.router.navigate(['/products']);
      },
      error: (err: any) => {
        this.errorMessage = err?.status === 403
          ? 'Account pending activation — an administrator must activate it before you can log in.'
          : 'Invalid username or password.';
      }
    });
  }
}
