import { Component } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule],
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
      <button mat-raised-button color="primary">Login</button>
    </form>
  </mat-card>
  `
})
export class LoginComponent {
  loginForm = this.fb.group({ email: [''], password: [''] });

  constructor(private fb: FormBuilder, private auth: AuthService, private router: Router) {}

  login() {

    const body = {
      username: this.loginForm.value.email, //map email field to username
      password: this.loginForm.value.password
    };
    this.auth.login(body).subscribe((res: any) => {
      this.auth.saveToken(res.token);
      this.router.navigate(['/products']);
    });
  }
}
