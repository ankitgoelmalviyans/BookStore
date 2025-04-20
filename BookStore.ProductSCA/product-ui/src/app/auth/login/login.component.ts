import { Component } from '@angular/core';
import { FormBuilder } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth.service';

@Component({
  selector: 'app-login',
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
